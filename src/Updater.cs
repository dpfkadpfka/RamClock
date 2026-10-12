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
        public const string Version = "1.0.1";   // 릴리스 때 태그 번호로 바뀜
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

        static void Check()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string json;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "RamClock";
                wc.Encoding = System.Text.Encoding.UTF8;
                json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
            }
            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            if (tag.Length == 0 || !Newer(tag, Version)) return;
            var url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+RamClock\\.exe)\"").Groups[1].Value;
            if (url.Length == 0) return;

            string dir = Path.GetDirectoryName(Exe), tmp = Path.Combine(dir, "RamClock.download");
            using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "RamClock"; wc.DownloadFile(url, tmp); }
            var fi = new FileInfo(tmp);
            byte[] head = new byte[2];
            using (var fs = File.OpenRead(tmp)) fs.Read(head, 0, 2);
            if (fi.Length < 500000 || head[0] != 'M' || head[1] != 'Z') { File.Delete(tmp); return; }
            // 실행 중인 파일은 이름은 바꿀 수 있어서: 지금 것 → .old, 새것 → 원래 이름
            if (File.Exists(OldExe)) File.Delete(OldExe);
            File.Move(Exe, OldExe);
            try { File.Move(tmp, Exe); }
            catch { File.Move(OldExe, Exe); throw; }
            Pending = tag.TrimStart('v', 'V');
            var r = Ready; if (r != null) r(Pending);
        }

        // 바로 새 버전으로 다시 켜기
        public static void Restart()
        {
            try { Process.Start(Exe, "--restart"); } catch (Exception ex) { Program.Log(ex); }
        }
    }
}
