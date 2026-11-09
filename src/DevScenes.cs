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
            Thread.Sleep(5500);
            RECT r; GetClientRect(h, out r); var p = new POINT(); ClientToScreen(h, ref p);
            client = new Rectangle(p.X, p.Y, r.R - r.L, r.B - r.T);
            L("app " + url + " " + client);
            return h;
        }

        static void CloseApp(IntPtr h) { if (h != IntPtr.Zero) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); Thread.Sleep(800); }

        static Settings Demo()
        {
            var S = Program.SampleSettings();
            S.Scale = 1.0f; S.X = 0; S.Y = 0;
            return S;
        }

        public static void Run(string outDir)
        {
            dir = outDir; Directory.CreateDirectory(dir); logPath = Path.Combine(dir, "log.txt");
            Application.EnableVisualStyles();
            Thread.Sleep(6000);   // 실행한 사람이 손을 뗄 시간
            var layer = new Layer(); layer.Show();
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
                    int x = cl.Right - c0.Content.Width / 2 - 50, y = cl.Y + 40;   // x는 시계 가운데
                    c0.Bmp.Dispose();
                    var c = Painter.Compose(S, st, now, 1f, x, y, work);
                    layer.Push(c.Bmp, c.Win.Location); c.Bmp.Dispose();
                    layer.BringToFront(); Application.DoEvents(); Thread.Sleep(900);
                    using (var b = Cap(cl)) b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
                    layer.Push(new Bitmap(1, 1), new Point(-10, -10));
                    CloseApp(h);
                    L("ok " + name);
                }
                catch (Exception ex) { L("FAIL " + name + " " + ex); }
            };
            var d = new DateTime(2026, 11, 10);
            string B = "https://dpfkadpfka.github.io/";
            scene("s01_clock", B + "jamo-play/", (S, st) => { }, d.AddHours(10).AddMinutes(25).AddSeconds(12));
            scene("s02_hover", B + "jangyeongsil-clock/", (S, st) => { st.Hover = true; }, d.AddHours(9).AddMinutes(20).AddSeconds(41));
            scene("s03_timer", B + "pizza-samgwon/", (S, st) => { S.Mode = 1; st.TDur = 300000; st.TRemain = 183000; st.Hover = true; }, d.AddHours(11).AddMinutes(5));
            scene("s04_banner", B + "yeram-ai-video/seat-race/", (S, st) => { st.BannerLabel = "조용히"; st.BannerText = "말하지 않습니다. 조용히 집중합니다."; st.BannerUntil = DateTime.MaxValue; }, d.AddHours(13).AddMinutes(12).AddSeconds(30));
            scene("s05_break", B + "jangyeongsil-clock/invent/", (S, st) => { st.BannerLabel = "쉬는 시간이에요"; st.BannerText = ""; st.BannerUntil = DateTime.MaxValue; }, d.AddHours(9).AddMinutes(40).AddSeconds(2));
            scene("s06_stopwatch", B + "jangyeongsil-clock/sundial/", (S, st) => { S.Mode = 2; st.WBase = 95430; st.Laps.Add(41210); st.Hover = true; }, d.AddHours(10).AddMinutes(50));
            scene("s07_meal", B, (S, st) => { st.BannerLabel = "오늘 급식"; st.BannerText = "기장밥  ·  쇠고기미역국  ·  돈육떡갈비  ·  배추김치  ·  요구르트"; st.BannerUntil = DateTime.MaxValue; }, d.AddHours(12).AddMinutes(10).AddSeconds(5));
            scene("s08_memo", B + "jamo-play/", (S, st) => { S.Memo = "오늘 준비물: 색연필, 가위\n5교시 강당 이동"; S.MemoBgOn = true; S.MemoBgColor = "#FFD3E0"; }, d.AddHours(8).AddMinutes(45).AddSeconds(10));
            scene("s09_msgs", B + "pizza-samgwon/", (S, st) => { st.Hover = true; st.Popup = 2; }, d.AddHours(10).AddMinutes(12).AddSeconds(8));
            layer.Close();
            File.WriteAllText(Path.Combine(dir, "done.txt"), "ok");
        }
    }
}
