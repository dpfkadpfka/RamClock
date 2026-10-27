using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 깃허브 릴리스에 새 버전이 올라오면 알아서 받아 바꿔 두기 (다음에 켤 때부터 새 버전)
    public static class Updater
    {
        public const string Repo = "dpfkadpfka/RamClock";
        public const string Version = "1.0.33";   // 릴리스 때 태그 번호로 바뀜
        public static Action<string> Ready;      // 새 버전 받아 둠 → 알림

        static string Exe { get { return Application.ExecutablePath; } }
        static string OldExe { get { return Path.Combine(Path.GetDirectoryName(Exe), Path.GetFileNameWithoutExtension(Exe) + ".old"); } }
        public static string Pending;            // 받아 둔 새 버전 번호

        public static void CleanUp() { try { if (File.Exists(OldExe)) File.Delete(OldExe); } catch { } }

        public static void Start()
        {
            var t = new Thread(() =>
            {
                Thread.Sleep(15000);
                while (true)
                {
                    try { Check(); } catch (Exception ex) { Program.Log(ex); }
                    if (Pending != null) return;
                    Thread.Sleep(TimeSpan.FromHours(6));
                }
            }) { IsBackground = true };
            t.Start();
        }

        static int[] Parse(string v)
        {
            var m = Regex.Matches(v ?? "", @"\d+");
            var r = new int[4];
            for (int i = 0; i < m.Count && i < 4; i++) int.TryParse(m[i].Value, out r[i]);
            return r;
        }
        static bool Newer(string a, string b)   // a가 b보다 새것?
        {
            var x = Parse(a); var y = Parse(b);
            for (int i = 0; i < 4; i++) if (x[i] != y[i]) return x[i] > y[i];
            return false;
        }

        // 메뉴의 [업데이트 확인]: 결과를 글로 알려 준다
        public static void CheckNow(Action<string> report)
        {
            new Thread(() =>
            {
                string msg;
                try
                {
                    if (Pending != null) msg = "새 버전 v" + Pending + "을 이미 받아 두었어요. [지금 업데이트]를 누르거나 다시 켜면 바뀌어요.";
                    else
                    {
                        string r = Check();
                        msg = r == null ? "지금 쓰는 v" + Version + "이 최신 버전이에요." : "새 버전 v" + r + "을 받았어요. [지금 업데이트]를 누르면 바로 바뀌어요.";
                    }
                }
                catch (Exception ex) { Program.Log(ex); msg = "업데이트를 확인하지 못했어요.\n(" + ex.Message + ")"; }
                if (report != null) report(msg);
            }) { IsBackground = true }.Start();
        }

        // 새 버전을 받아 바꿔 두면 그 버전 번호, 없으면 null
        static readonly object gate = new object();
        static string Check()
        {
            lock (gate) return CheckCore();
        }

        static string CheckCore()
        {
            if (Pending != null) return Pending;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            // 1) github.com/…/releases/latest 가 넘겨주는 주소에서 버전 읽기 (API처럼 횟수 제한이 없어 학교처럼 여럿이 같은 인터넷을 써도 됨)
            string tag = LatestTagByRedirect();
            if (string.IsNullOrEmpty(tag))
            {
                // 2) 안 되면 API로
                string json;
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "RamClock";
                    wc.Encoding = System.Text.Encoding.UTF8;
                    json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                }
                tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            }
            if (tag.Length == 0 || !Newer(tag, Version)) return null;
            var url = "https://github.com/" + Repo + "/releases/download/" + tag + "/RamClock.exe";

            string dir = Path.GetDirectoryName(Exe), tmp = Path.Combine(dir, "RamClock.download");
            using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "RamClock"; wc.DownloadFile(url, tmp); }
            var fi = new FileInfo(tmp);
            byte[] head = new byte[2];
            using (var fs = File.OpenRead(tmp)) fs.Read(head, 0, 2);
            if (fi.Length < 500000 || head[0] != 'M' || head[1] != 'Z') { File.Delete(tmp); return null; }
            // 실행 중인 파일은 이름은 바꿀 수 있어서: 지금 것 → .old, 새것 → 원래 이름
            if (File.Exists(OldExe)) File.Delete(OldExe);
            File.Move(Exe, OldExe);
            try { File.Move(tmp, Exe); }
            catch { File.Move(OldExe, Exe); throw; }
            Pending = tag.TrimStart('v', 'V');
            var r = Ready; if (r != null) r(Pending);
            return Pending;
        }

        static string LatestTagByRedirect()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://github.com/" + Repo + "/releases/latest");
                req.AllowAutoRedirect = false; req.Method = "HEAD"; req.UserAgent = "RamClock"; req.Timeout = 15000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    string loc = resp.Headers["Location"] ?? "";
                    int i = loc.LastIndexOf("/tag/");
                    return i >= 0 ? Uri.UnescapeDataString(loc.Substring(i + 5)).Trim('/') : null;
                }
            }
            catch (Exception ex) { Program.Log(ex); return null; }
        }

        // 바로 새 버전으로 다시 켜기
        public static void Restart()
        {
            try { Process.Start(Exe, "--restart"); } catch (Exception ex) { Program.Log(ex); }
        }
    }
}
