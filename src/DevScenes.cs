using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 개발용: 실제 수업 화면(웹앱) 위에 투명 위젯을 진짜 레이어드 창으로 띄우고 화면을 그대로 찍는다.
    // 위젯 뒤로 실제 화면이 비쳐 보이는 "HUD" 모습 그대로. (가짜 명단만 씀, 설정 저장 안 함)
    static class DevScenes
    {
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        static string dir, logPath;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        public static bool Desk;   // 웹앱 대신 실제 바탕화면 위에서 찍기
        static IntPtr DefView()
        {
            IntPtr dv = FindWindowEx(FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero) return dv;
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
            { dv = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null); if (dv != IntPtr.Zero) return dv; }
            return IntPtr.Zero;
        }
        static bool IconsShown() { var dv = DefView(); var lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null); return lv != IntPtr.Zero && IsWindowVisible(lv); }
        static void ToggleIcons() { var dv = DefView(); if (dv != IntPtr.Zero) SendMessage(dv, 0x0111, new IntPtr(0x7402), IntPtr.Zero); Thread.Sleep(500); }
        static List<IntPtr> hidden = new List<IntPtr>();
        // 바탕화면만 보이도록 다른 프로그램 창(달력 위젯·메신저 등 포함)을 잠깐 숨긴다 — 끝나면 그대로 다시 보이게
        static void HideOthers()
        {
            uint me = (uint)Process.GetCurrentProcess().Id;
            var skip = new HashSet<string> { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                uint pid; GetWindowThreadProcessId(h, out pid); if (pid == me) return true;
                var c = new StringBuilder(64); GetClassName(h, c, 64); if (skip.Contains(c.ToString())) return true;
                hidden.Add(h); return true;
            }, IntPtr.Zero);
            foreach (var h in hidden) ShowWindow(h, 0);
            L("hid " + hidden.Count);
        }
        static void ShowOthers() { foreach (var h in hidden) ShowWindow(h, 8); hidden.Clear(); }
        static object ShellApp() { return Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")); }
        static void ShellCall(string m) { try { var o = ShellApp(); o.GetType().InvokeMember(m, System.Reflection.BindingFlags.InvokeMethod, null, o, null); } catch (Exception ex) { L("shell " + m + " " + ex.Message); } }
        static void L(string m) { try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss ") + m + "\r\n"); } catch { } }

        static Bitmap Cap(Rectangle r)
        {
            var b = new Bitmap(r.Width, r.Height);
            using (var g = Graphics.FromImage(b))
            {
                IntPtr d = g.GetHdc(), s = GetDC(IntPtr.Zero);
                BitBlt(d, 0, 0, r.Width, r.Height, s, r.X, r.Y, 0x00CC0020 | 0x40000000);
                ReleaseDC(IntPtr.Zero, s); g.ReleaseHdc(d);
            }
            return b;
        }

        // 진짜 투명 위젯 창
        class Layer : Form
        {
            public Layer() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; }
            protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x8; return cp; } }
            protected override bool ShowWithoutActivation { get { return true; } }
            [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SZ psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BF pblend, int dwFlags);
            [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
            [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
            [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr o);
            [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
            [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; }
            [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BF { public byte Op, Flags, Alpha, Format; }
            public void Push(Bitmap bmp, Point pos)
            {
                IntPtr sdc = GetDC(IntPtr.Zero), mdc = CreateCompatibleDC(sdc), hb = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mdc, hb);
                var size = new SZ { cx = bmp.Width, cy = bmp.Height }; var src = new POINT(); var top = new POINT { X = pos.X, Y = pos.Y };
                var bf = new BF { Alpha = 255, Format = 1 };
                UpdateLayeredWindow(Handle, sdc, ref top, ref size, mdc, ref src, 0, ref bf, 2);
                SelectObject(mdc, old); DeleteObject(hb); DeleteDC(mdc); ReleaseDC(IntPtr.Zero, sdc);
            }
        }

        static HashSet<IntPtr> ChromeWins()
        {
            var set = new HashSet<IntPtr>();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                var c = new StringBuilder(64); GetClassName(h, c, 64);
                if (c.ToString().StartsWith("Chrome_WidgetWin")) set.Add(h);
                return true;
            }, IntPtr.Zero);
            return set;
        }

        // 크롬 앱 창으로 웹앱을 주 모니터에 꽉 채워 띄우고 그 안쪽(주소창 없는 화면) 사각형을 돌려준다
        static IntPtr OpenApp(string url, out Rectangle client)
        {
            if (Desk) { client = Screen.PrimaryScreen.WorkingArea; Thread.Sleep(600); L("desk " + client); return new IntPtr(1); }
            client = Rectangle.Empty;
            var before = ChromeWins();
            Process.Start(new ProcessStartInfo("chrome.exe", "--new-window --app=" + url) { UseShellExecute = true });
            IntPtr h = IntPtr.Zero;
            for (int i = 0; i < 40 && h == IntPtr.Zero; i++)
            {
                Thread.Sleep(300);
                foreach (var w in ChromeWins()) if (!before.Contains(w)) { var t = new StringBuilder(256); GetWindowText(w, t, 256); if (t.Length > 0) { h = w; break; } }
            }
            if (h == IntPtr.Zero) { L("no app window " + url); return h; }
            var pb = Screen.PrimaryScreen.Bounds;
            ShowWindow(h, 9); SetWindowPos(h, IntPtr.Zero, pb.X + 40, pb.Y + 40, 1200, 800, 0x0040);
            Thread.Sleep(300); ShowWindow(h, 3); SetForegroundWindow(h);
            SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0003 | 0x0010);   // 다른 '항상 위' 창(메신저 등)보다 위로 — 다른 창이 찍히지 않게
            Thread.Sleep(5500);
            RECT r; GetClientRect(h, out r); var p = new POINT(); ClientToScreen(h, ref p);
            client = new Rectangle(p.X, p.Y, r.R - r.L, r.B - r.T);
            L("app " + url + " " + client);
            return h;
        }

        static void CloseApp(IntPtr h) { if (Desk) return; if (h != IntPtr.Zero) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); Thread.Sleep(800); }

        static Settings Demo()
        {
            var S = Program.SampleSettings();
            S.Scale = 1.0f; S.X = 0; S.Y = 0;
            return S;
        }

        // 웹앱 화면을 띄우고 그 위에 실제 창을 열어 찍는다. step(0): 준비됐는지, step(1): 내용 채우기
        static void FormScene(string name, string url, Func<Rectangle, Form> make, Func<Form, int, bool> step, int waitMs = 2500)
        {
            try
            {
                Rectangle cl; var h = OpenApp(url, out cl);
                if (h == IntPtr.Zero) return;
                var f = make(cl);
                int st = 0, wait = 0;
                var t = new System.Windows.Forms.Timer { Interval = 250 };
                t.Tick += (o, e) =>
                {
                    try
                    {
                        if (st == 0) { if (!step(f, 0) && ++wait < 80) return; st = 1; step(f, 1); wait = 0; return; }
                        if (st == 1) { wait += 250; if (wait == 1000) { SetWindowPos(f.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0003 | 0x0010); f.BringToFront(); f.Activate(); } if (wait < waitMs) return; st = 2; using (var b = Cap(cl)) b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png); t.Stop(); f.Close(); }
                    }
                    catch (Exception ex) { L("tick " + name + " " + ex.Message); t.Stop(); f.Close(); }
                };
                f.Shown += (o, e) => { f.Activate(); SetWindowPos(f.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0003 | 0x0010); t.Start(); };
                Application.Run(f);
                CloseApp(h);
                L("ok " + name);
            }
            catch (Exception ex) { L("FAIL " + name + " " + ex); }
        }

        public static bool OnlyForms;
        public static void Run(string outDir)
        {
            dir = outDir; Directory.CreateDirectory(dir); logPath = Path.Combine(dir, "log.txt");
            Application.EnableVisualStyles();
            Thread.Sleep(6000);   // 실행한 사람이 손을 뗄 시간
            try { string why; bool okw = Wv2.Available(out why); L("wv2 " + okw + " " + why); } catch (Exception ex) { L("wv2 ex " + ex); }   // 투표 창(WebView2) 준비
            bool hidIcons = false;
            if (Desk) { HideOthers(); Thread.Sleep(1200); if (IconsShown()) { ToggleIcons(); hidIcons = true; } L("icons hidden " + hidIcons); }
            try { RunAll(); } catch (Exception ex) { L("run " + ex); } finally { if (hidIcons) ToggleIcons(); if (Desk) ShowOthers(); L("restored"); }
            File.WriteAllText(Path.Combine(dir, "done.txt"), "ok");
        }
        static void RunAll()
        {
            var layer = new Layer(); layer.Show();
            string B = "https://dpfkadpfka.github.io/";
            if (!OnlyForms) {
            Action<string, string, Action<Settings, HudState>, DateTime> scene = (name, url, setup, now) =>
            {
                try
                {
                    Rectangle cl; var h = OpenApp(url, out cl);
                    if (h == IntPtr.Zero) return;
                    var S = Demo(); var st = new HudState(); setup(S, st);
                    var work = Screen.PrimaryScreen.WorkingArea;
                    // 시계를 오른쪽 위에 (실제로 쓰는 자리)
                    var c0 = Painter.Compose(S, st, now, 1f, 0, 0, work);
                    int x = cl.Right - Math.Max(c0.Content.Width, c0.Win.Width) / 2 - 40, y = cl.Y + 40;   // x는 시계 가운데 (버튼까지 화면 안에)
                    c0.Bmp.Dispose();
                    var c = Painter.Compose(S, st, now, 1f, x, y, work);
                    layer.Push(c.Bmp, c.Win.Location); c.Bmp.Dispose();
                    SetWindowPos(layer.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0003 | 0x0010);
                    layer.BringToFront(); Application.DoEvents(); Thread.Sleep(900);
                    using (var b = Cap(cl)) b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
                    layer.Push(new Bitmap(1, 1), new Point(-10, -10));
                    CloseApp(h);
                    L("ok " + name);
                }
                catch (Exception ex) { L("FAIL " + name + " " + ex); }
            };
            var d = new DateTime(2026, 11, 10);
            scene("s01_clock", B + "jamo-play/", (S, st) => { }, d.AddHours(10).AddMinutes(25).AddSeconds(12));
            scene("s02_hover", B + "jangyeongsil-clock/", (S, st) => { st.Hover = true; }, d.AddHours(9).AddMinutes(20).AddSeconds(41));
            scene("s03_timer", B + "pizza-samgwon/", (S, st) => { S.Mode = 1; st.TDur = 300000; st.TRemain = 183000; st.Hover = true; }, d.AddHours(11).AddMinutes(5));
            scene("s04_banner", B + "yeram-ai-video/seat-race/", (S, st) => { st.BannerLabel = "조용히"; st.BannerText = "말하지 않습니다. 조용히 집중합니다."; st.BannerUntil = DateTime.MaxValue; }, d.AddHours(13).AddMinutes(12).AddSeconds(30));
            scene("s05_break", B + "jangyeongsil-clock/invent/", (S, st) => { st.BannerLabel = "쉬는 시간이에요"; st.BannerText = ""; st.BannerUntil = DateTime.MaxValue; }, d.AddHours(9).AddMinutes(40).AddSeconds(2));
            scene("s06_stopwatch", B + "jangyeongsil-clock/sundial/", (S, st) => { S.Mode = 2; st.WBase = 95430; st.Laps.Add(41210); st.Hover = true; }, d.AddHours(10).AddMinutes(50));
            scene("s07_meal", B, (S, st) => { st.BannerLabel = "오늘 급식"; st.BannerText = "기장밥  ·  쇠고기미역국  ·  돈육떡갈비  ·  배추김치  ·  요구르트"; st.BannerUntil = DateTime.MaxValue; }, d.AddHours(12).AddMinutes(10).AddSeconds(5));
            scene("s08_memo", B + "jamo-play/", (S, st) => { S.Memo = "오늘 준비물: 색연필, 가위\n5교시 강당 이동"; S.MemoBgOn = true; S.MemoBgColor = "#FFD3E0"; }, d.AddHours(8).AddMinutes(45).AddSeconds(10));
            scene("s09_msgs", B + "pizza-samgwon/", (S, st) => { st.Hover = true; st.Popup = 2; }, d.AddHours(10).AddMinutes(12).AddSeconds(8));
            }
            layer.Close();
            // 실제 창(투표·뽑기)을 수업 화면 위에 띄워 찍기
            FormScene("s10_vote", B + "jamo-play/", (cl) => {
                var vf = new VoteForm(Program.SampleSettings(), 1f) { StartPosition = FormStartPosition.Manual };
                vf.Bounds = new Rectangle(cl.X + (cl.Width - 1200) / 2, cl.Y + (cl.Height - 720) / 2, 1200, 720);
                return vf;
            }, (f, step) => {
                var fr = typeof(VoteForm).GetField("ready", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var mr = typeof(VoteForm).GetMethod("Run", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (step == 0) return (bool)fr.GetValue(f);
                if (step == 1) mr.Invoke(f, new object[] { "showVote('https://dpfkadpfka.github.io/RamClock/v/?c=demo&id=demo01','오늘 체육 시간에 뭐 할까?','c',['피구','줄넘기','술래잡기','발야구']);setResults([9,3,7,5],24);" });
                return true;
            });
            Thread.Sleep(3000);
            FormScene("s11_opinion", B + "pizza-samgwon/", (cl) => {
                var vf = new VoteForm(Program.SampleSettings(), 1f) { StartPosition = FormStartPosition.Manual };
                vf.Bounds = new Rectangle(cl.X + (cl.Width - 1200) / 2, cl.Y + (cl.Height - 720) / 2, 1200, 720);
                return vf;
            }, (f, step) => {
                var fr = typeof(VoteForm).GetField("ready", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var mr = typeof(VoteForm).GetMethod("Run", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (step == 0) return (bool)fr.GetValue(f);
                if (step == 1) mr.Invoke(f, new object[] { "showVote('https://dpfkadpfka.github.io/RamClock/v/?c=demo&id=demo02','우리 반 가을 소풍 때 하고 싶은 놀이는?','t',[]);setTexts(['보물찾기 하고 싶어요','돗자리 펴고 다 같이 도시락 먹기','수건돌리기!','낙엽 모아서 그림 만들기','이어달리기 시합','사진 많이 찍기']);" });
                return true;
            });
            FormScene("s12_pick", B + "jangyeongsil-clock/", (cl) => {
                var pf = new PickerForm(Program.SampleSettings(), null, 1f, new Rectangle(cl.X + (cl.Width - 1300) / 2, cl.Y + (cl.Height - 820) / 2, 1300, 820));
                return pf;
            }, (f, step) => { if (step == 1) ((PickerForm)f).DrawNow(); return true; }, 4500);
        }
    }
}
