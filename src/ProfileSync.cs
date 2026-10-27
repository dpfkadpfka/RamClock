using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ClassroomHud
{
    // 학생이 자기 태블릿에서 보낸 카드(번호·이름·닉네임·사진)를 받아 오기
    // 학생 페이지 → ntfy.sh(우리 반 비밀 주소) → RamClock이 가져가서 명단에 넣음
    public static class ProfileSync
    {
        public const string PageUrl = "https://dpfkadpfka.github.io/RamClock/p/";

        // 받는 서버 (시험할 때만 RAMCLOCK_NTFY로 바꿈)
        static string Server { get { var e = Environment.GetEnvironmentVariable("RAMCLOCK_NTFY"); return string.IsNullOrEmpty(e) ? "https://ntfy.sh" : e; } }

        public class Update { public int No; public string Name, Nick, Photo; }

        public static string Topic(Settings S) { return "ramclock-" + S.ClassCode; }

        public static string StudentLink(Settings S)
        {
            return PageUrl + "?c=" + S.ClassCode + "&n=" + S.StudentCount + "&t=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(S.ClassTitle) ? "우리 반" : S.ClassTitle.Trim());
        }

        static int busy;
        public static string LastError;   // 마지막으로 받기에 실패한 이유 (없으면 null)

        // 새로 온 카드들을 받아 done(목록)으로 넘김 (백그라운드)
        public static void Poll(string topic, string since, Action<List<Update>, string> done)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            var t = new Thread(() =>
            {
                var list = new List<Update>(); string last = since;
                LastError = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                    string url = Server + "/" + topic + "/json?poll=1&since=" + (string.IsNullOrEmpty(since) ? "12h" : since);
                    string body;
                    using (var wc = new WebClient()) { wc.Encoding = Encoding.UTF8; wc.Headers[HttpRequestHeader.UserAgent] = "RamClock"; body = wc.DownloadString(url); }
                    var js = new JavaScriptSerializer();
                    foreach (var line in body.Split('\n'))
                    {
                        if (line.Trim().Length == 0) continue;
                        Dictionary<string, object> m;
                        try { m = js.Deserialize<Dictionary<string, object>>(line); } catch { continue; }
                        if (!m.ContainsKey("event") || (m["event"] as string) != "message") continue;
                        last = m["id"] as string ?? last;
                        Dictionary<string, object> d;
                        try { d = js.Deserialize<Dictionary<string, object>>(m.ContainsKey("message") ? m["message"] as string : "{}"); } catch { continue; }
                        var u = new Update();
                        try { u.No = Convert.ToInt32(d["no"]); } catch { continue; }
                        u.Name = d.ContainsKey("name") ? (d["name"] as string ?? "").Trim() : "";
                        u.Nick = d.ContainsKey("nick") ? (d["nick"] as string ?? "").Trim() : "";
                        if (u.Nick.Length > 12) u.Nick = u.Nick.Substring(0, 12);
                        if (m.ContainsKey("attachment"))
                        {
                            var a = m["attachment"] as Dictionary<string, object>;
                            string aurl = a != null && a.ContainsKey("url") ? a["url"] as string : null;
                            if (!string.IsNullOrEmpty(aurl))
                            {
                                try
                                {
                                    Directory.CreateDirectory(Settings.MediaDir);
                                    string name = "student_" + u.No + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".jpg";
                                    using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "RamClock"; wc.DownloadFile(aurl, Path.Combine(Settings.MediaDir, name)); }
                                    u.Photo = name;
                                }
                                catch (Exception ex) { Program.Log(ex); LastError = "사진을 받지 못했어요 (" + ex.Message + ")"; }
                            }
                        }
                        list.Add(u);
                    }
                }
                catch (Exception ex) { Program.Log(ex); LastError = "카드를 받아 오지 못했어요 — 이 컴퓨터에서 ntfy.sh 접속이 막혀 있을 수 있어요 (" + ex.Message + ")"; }
                finally { Interlocked.Exchange(ref busy, 0); }
                try { done(list, last); } catch { }
            }) { IsBackground = true };
            t.Start();
        }

        static string Norm(string s) { return (s ?? "").Replace(" ", "").Trim(); }

        // 받은 카드를 명단에 넣기. 명단에 이름이 있는데 다른 이름을 쓰면 무시 (남의 번호로 바꾸는 장난 막기)
        // 돌려주는 값: 화면에 띄울 안내 글들
        public static List<string> Apply(Settings S, List<Update> list)
        {
            var msgs = new List<string>();
            foreach (var u in list)
            {
                if (u.No < 1 || u.No > S.StudentCount) continue;
                var st = S.StudentOf(u.No);
                if (st != null && !string.IsNullOrWhiteSpace(st.Name) && u.Name.Length > 0 && Norm(st.Name) != Norm(u.Name))
                {
                    msgs.Add(u.No + "번: 이름이 명단(" + st.Name.Trim() + ")과 달라서 받지 않았어요");
                    continue;
                }
                if (st == null) { st = new Student(u.No, u.Name); S.Students.Add(st); S.Students.Sort((a, b) => a.No.CompareTo(b.No)); }
                if (string.IsNullOrWhiteSpace(st.Name) && u.Name.Length > 0) st.Name = u.Name;
                st.Nick = u.Nick.Length > 0 ? u.Nick : null;
                if (u.Photo != null) st.Photo = u.Photo;
                msgs.Add(u.No + "번 " + (st.Nick ?? st.Name ?? "") + " 카드가 바뀌었어요" + (u.Photo != null ? " (사진 포함)" : ""));
            }
            return msgs;
        }
    }
}
