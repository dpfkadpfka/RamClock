using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 개발용: README·가이드에 넣을 화면을 실제 윈도우에서 찍는다.
    // 파일 이름을 RamClockShots.exe로 두고 실행하면 옆의 out 폴더에 PNG를 저장한다.
    // (가짜 명단만 쓰고 실제 설정은 저장하지 않는다)
    static class DevShots
    {
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
                [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
        [DllImport("kernel32.dll")] static extern bool WriteFile(IntPtr h, byte[] b, uint n, out uint w, IntPtr o);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        static string dir, logPath;
        static void L(string m) { try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss ") + m + "\r\n"); } catch { } }

        public static void Run(string outDir, string only)
        {
            if (!string.IsNullOrEmpty(only)) outDir += "_" + only;
            dir = outDir; Directory.CreateDirectory(dir); logPath = Path.Combine(dir, "log.txt");
            Application.EnableVisualStyles();
            foreach (var p in Process.GetProcesses())
                try { if (p.ProcessName.StartsWith("RamClockShots", StringComparison.OrdinalIgnoreCase) && p.Id != Process.GetCurrentProcess().Id) p.Kill(); } catch { }
            if (string.IsNullOrEmpty(only) || only == "real" || only == "b2") Step("real", RealWidget);
            if (string.IsNullOrEmpty(only) || only == "guide") Step("guide", () => { Directory.CreateDirectory(Path.Combine(dir, "w")); Program.GuideShotsPublic(Path.Combine(dir, "w")); });
            if (string.IsNullOrEmpty(only) || only == "vote") Step("vote", Vote);
            if (string.IsNullOrEmpty(only) || only == "forms") Step("forms", () => { Directory.CreateDirectory(Path.Combine(dir, "f")); Program.ShotsPublic(Path.Combine(dir, "f")); });
            if (only == "ss2") { Step("gh2", Gh2); Step("smartscreen", SmartScreen); File.WriteAllText(Path.Combine(dir, "done.txt"), "ok"); return; }
            if (only == "tail" || only == "b2") Step("tail", () => { Directory.CreateDirectory(Path.Combine(dir, "f")); Program.ShotsTailOnly = true; Program.ShotsPublic(Path.Combine(dir, "f")); });
            if (string.IsNullOrEmpty(only) || only == "github" || only == "b2") Step("github", GitHub);
            File.WriteAllText(Path.Combine(dir, "phase1.txt"), "ok");
            if (string.IsNullOrEmpty(only) || only == "smartscreen" || only == "b2") Step("smartscreen", SmartScreen);
            File.WriteAllText(Path.Combine(dir, "done.txt"), "ok");
        }

        static void Step(string name, Action a)
        {
            L("start " + name);
            try { a(); L("ok " + name); } catch (Exception ex) { L("FAIL " + name + " " + ex); }
        }

        static long Sig(Rectangle r)
        {
            using (var b = Cap(r)) { long v = r.Height * 1000L; for (int y = 0; y < b.Height; y += 6) for (int x = 0; x < b.Width; x += 6) { var c = b.GetPixel(x, y); v += (c.R + c.G + c.B) / 60; } return v; }
        }

        static Bitmap Cap(Rectangle r)
        {
            var b = new Bitmap(r.Width, r.Height);
            using (var g = Graphics.FromImage(b))
            {
                IntPtr dst = g.GetHdc(), src = GetDC(IntPtr.Zero);
                BitBlt(dst, 0, 0, r.Width, r.Height, src, r.X, r.Y, 0x00CC0020 | 0x40000000);
                ReleaseDC(IntPtr.Zero, src); g.ReleaseHdc(dst);
            }
            return b;
        }
        static void Save(Bitmap b, string name) { b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png); b.Dispose(); }

        static List<KeyValuePair<IntPtr, Rectangle>> Windows(Func<uint, string, bool> want)
        {
            var list = new List<KeyValuePair<IntPtr, Rectangle>>();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                if (!want(pid, sb.ToString())) return true;
                RECT r; GetWindowRect(h, out r);
                if (r.R - r.L > 40 && r.B - r.T > 40) list.Add(new KeyValuePair<IntPtr, Rectangle>(h, Rectangle.FromLTRB(r.L, r.T, r.R, r.B)));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        // 1) 지금 켜져 있는 진짜 RamClock 위젯
        static void RealWidget()
        {
            var me = Process.GetCurrentProcess().Id;
            var pids = new HashSet<uint>();
            foreach (var p in Process.GetProcessesByName("RamClock")) if (p.Id != me) pids.Add((uint)p.Id);
            L("ramclock pids " + pids.Count);
            var ws = Windows((pid, t) => pids.Contains(pid));
            if (ws.Count == 0) { L("no widget window"); return; }
            ws.Sort((a, b) => (b.Value.Width * b.Value.Height).CompareTo(a.Value.Width * a.Value.Height));
            var r = ws[0].Value; L("widget " + r);
            File.WriteAllText(Path.Combine(dir, "real_rect.txt"), r.X + "," + r.Y + "," + r.Width + "," + r.Height);
            var scr = Screen.FromRectangle(r).Bounds;
            Func<Rectangle, int, Rectangle> grow = (x, m) => Rectangle.Intersect(scr, Rectangle.Inflate(x, m, m));
            SetCursorPos(scr.Right - 5, scr.Bottom - 200); Thread.Sleep(1500);
            Save(Cap(grow(r, 30)), "real_widget");
            Save(Cap(grow(r, 260)), "real_context");
            SetCursorPos(r.X + r.Width / 2, r.Y + r.Height / 2); Thread.Sleep(1500);
            Save(Cap(grow(r, 30)), "real_hover");
            SetCursorPos(scr.Right - 5, scr.Bottom - 200); Thread.Sleep(300);
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        static void Gh2()
        {
            var ws = Windows((pid, t) => t.Contains("Release") && t.Contains("RamClock"));
            if (ws.Count == 0) { L("no gh window"); return; }
            var h = ws[0].Key; ShowWindow(h, 6); Thread.Sleep(300); ShowWindow(h, 9); SetForegroundWindow(h); Thread.Sleep(1500);
            RECT r; GetWindowRect(h, out r); var rr = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            rr = Rectangle.Intersect(Screen.FromRectangle(rr).Bounds, rr); L("gh " + rr);
            Save(Cap(rr), "github");
        }

        static void GitHub()
        {
            Process.Start(new ProcessStartInfo("https://github.com/dpfkadpfka/RamClock/releases/latest") { UseShellExecute = true });
            Thread.Sleep(9000);
            var h = GetForegroundWindow(); RECT r; GetWindowRect(h, out r);
            var rr = Rectangle.FromLTRB(r.L, r.T, r.R, r.B); L("fg " + rr);
            rr = Rectangle.Intersect(Screen.FromRectangle(rr).Bounds, rr);
            Save(Cap(rr), "github");
        }

        // 2) 투표 창 (WebView2)
        static void Vote()
        {
            string why;
            if (!Wv2.Available(out why)) { L("wv2 " + why); return; }
            Program.VoteShots(dir);
        }

        // 3) "Windows의 PC 보호" 창: 인터넷에서 받은 표시를 붙인 복사본을 실행해 본다 (--noop라 실행돼도 바로 꺼짐)
        static void SmartScreen()
        {
            string sd = Path.Combine(dir, "ss"); Directory.CreateDirectory(sd);
            string exe = Path.Combine(sd, "RamClock.exe");
            try { File.Copy(Application.ExecutablePath, exe, true); } catch (Exception ex) { L("copy " + ex.Message); }
            var h = CreateFile(exe + ":Zone.Identifier", 0x40000000, 0, IntPtr.Zero, 2, 0x80, IntPtr.Zero);
            if (h != new IntPtr(-1))
            {
                var bytes = Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://github.com/dpfkadpfka/RamClock/releases\r\nHostUrl=https://github.com/dpfkadpfka/RamClock/releases/latest/download/RamClock.exe\r\n");
                uint w; WriteFile(h, bytes, (uint)bytes.Length, out w, IntPtr.Zero); CloseHandle(h);
            }
            else L("zone write fail " + Marshal.GetLastWin32Error());
            bool already = Windows((pid, t) => t.Contains("PC 보호") || t.Contains("SmartScreen")).Count > 0;
            if (!already) new Thread(() => { try { Process.Start(new ProcessStartInfo(exe, "--noop") { UseShellExecute = true, WorkingDirectory = sd }); } catch (Exception ex) { L("start " + ex.Message); } }) { IsBackground = true }.Start();
            var scr = Screen.PrimaryScreen.Bounds;
            Rectangle wr = Rectangle.Empty; IntPtr wh = IntPtr.Zero;
            for (int i = 0; i < 40 && wh == IntPtr.Zero; i++)
            {
                Thread.Sleep(500);
                var ws = Windows((pid, t) => t.Contains("PC 보호") || t.Contains("SmartScreen"));
                if (ws.Count > 0) { wh = ws[0].Key; wr = ws[0].Value; }
            }
            if (wh == IntPtr.Zero) { L("no smartscreen window"); Save(Cap(scr), "ss_full_none"); return; }
            Thread.Sleep(1200);
            L("ss rect " + wr);
            File.WriteAllText(Path.Combine(dir, "ss_rect.txt"), wr.X + "," + wr.Y + "," + wr.Width + "," + wr.Height);
            Save(Cap(wr), "ss_1");
            Save(Cap(scr), "ss_1_full");
            // 보안 창은 프로그램이 누르지 않는다. 선생님이 [추가 정보]를 누르면 바뀐 화면을 저장한다.
            long last = Sig(wr); int n = 2;
            var until = DateTime.Now.AddMinutes(30);
            while (DateTime.Now < until)
            {
                Thread.Sleep(600);
                var ws = Windows((pid, t) => t.Contains("PC 보호") || t.Contains("SmartScreen"));
                if (ws.Count == 0) { L("ss window closed"); break; }
                wr = ws[0].Value;
                long sg = Sig(wr);
                if (Math.Abs(sg - last) > 2000) { Thread.Sleep(700); wr = Windows((pid, t) => t.Contains("PC 보호") || t.Contains("SmartScreen"))[0].Value; Save(Cap(wr), "ss_" + n++); last = Sig(wr); L("ss changed " + wr); }
            }
        }
    }
}
