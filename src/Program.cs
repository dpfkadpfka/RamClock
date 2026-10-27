using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("RamClock")]
[assembly: AssemblyProduct("RamClock (예람쌤)")]
[assembly: AssemblyDescription("교실 시계·시간표·타이머·스톱워치 위젯")]
[assembly: AssemblyVersion(ClassroomHud.Updater.Version + ".0")]
[assembly: AssemblyFileVersion(ClassroomHud.Updater.Version + ".0")]

namespace ClassroomHud
{
    public static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);

        // 모니터마다 배율이 달라도 선명하게: 모니터별 DPI 인식
        static void DpiAware()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 0 || args[0] != "--shots") DpiAware();
            if (args.Length >= 2 && args[0] == "--render-test") { RenderTest(args[1]); return; }
            if (args.Length >= 2 && args[0] == "--tpl")
            {
                RosterImport.WriteTemplate(args[1], new List<string[]> { new[] { "1", "김하늘", "여" }, new[] { "2", "이도윤", "남" }, new[] { "3", "", "" } });
                return;
            }
            if (args.Length >= 1 && args[0] == "--times")
            {
                foreach (var t in new[] { "0945", "945", "9", "09:45", "9시45분", "9시", "오후 1:30", "1330", "25:00", "abc" }) Console.WriteLine(t + " -> " + Sched.ToMin(t) + " " + (Sched.ToMin(t) >= 0 ? Sched.FromMin(Sched.ToMin(t)) : "X"));
                return;
            }
            if (args.Length >= 2 && args[0] == "--roster")
            {
                string m; var rows = RosterImport.Read(args[1], out m);
                Console.WriteLine("msg: " + m);
                if (rows != null) foreach (var r in rows) Console.WriteLine(r.No + " " + r.Name + " " + r.Gender + " photo=" + (r.Photo == null ? 0 : r.Photo.Length) + r.PhotoExt);
                return;
            }
            if (args.Length >= 2 && args[0] == "--parse" && (args[1].EndsWith(".hwpx") || args[1].EndsWith(".hwp")))
            {
                var hr = HwpxWeek.Parse(args[1]);
                Console.WriteLine("msg: " + hr.Message + " periods " + hr.Periods + " monday " + hr.Monday);
                foreach (var kv in hr.Days) { Console.WriteLine("== " + kv.Key); foreach (var c in kv.Value) Console.WriteLine("   [" + c.Subj + "] " + (c.Content ?? "").Replace("\n", " / ")); }
                return;
            }
            if (args.Length >= 2 && args[0] == "--parse")
            {
                var pdf = new PdfText(System.IO.File.ReadAllBytes(args[1]));
                Console.WriteLine("error: " + pdf.Error);
                var runs = pdf.Extract();
                Console.WriteLine("runs: " + runs.Count + " hl " + pdf.HLines.Count + " vl " + pdf.VLines.Count);
                if (args.Length > 2) foreach (var x in runs) Console.WriteLine(x.X.ToString("0") + "," + x.Y.ToString("0") + " " + x.Text);
                var res = WeekParser.Parse(runs, pdf.HLines, pdf.VLines);
                Console.WriteLine("msg: " + res.Message + " periods " + res.Periods + " monday " + res.Monday);
                foreach (var kv in res.Days) { Console.WriteLine("== " + kv.Key); foreach (var c in kv.Value) Console.WriteLine("   [" + c.Subj + "] " + (c.Content ?? "").Replace("\n", " / ")); }
                return;
            }
            if (args.Length >= 2 && args[0] == "--shots") { Shots(args[1]); return; }
            bool created;
            using (var m = new Mutex(true, "RamClock-SingleInstance", out created))
            {
                if (!created && Array.IndexOf(args, "--restart") >= 0)
                {
                    try { created = m.WaitOne(10000); } catch (AbandonedMutexException) { created = true; }
                }
                if (!created)
                {
                    MessageBox.Show("RamClock이 이미 실행 중이에요.\n작업 표시줄 오른쪽 아래 아이콘을 확인해 주세요.", "RamClock");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // 예상 못 한 오류가 나도 꺼지지 않고 기록만 남기기
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (o, e) => Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Log(e.ExceptionObject as Exception);
                Application.Run(new HudForm());
                GC.KeepAlive(m);
            }
        }

        // 개발용: 창 없이 그림만 PNG로 저장
        static void RenderTest(string dir)
        {
            var S = Settings.Defaults();
            var st = new HudState();
            var t = new DateTime(2026, 10, 8, 9, 45, 52);
            var work = new Rectangle(0, 0, 900, 640);
            Save(dir + "/1_clock.png", S, st, t, work, 0);
            st.Hover = true; Save(dir + "/2_hover.png", S, st, t, work, 0);
            st.Hover = false; S.Memo = "오늘 준비물: 색연필, 가위\n5교시 강당 이동"; Save(dir + "/2b_memo.png", S, st, t, work, 1); S.Memo = ""; st.Hover = true;
            S.Mode = 1; st.TRemain = 183000; st.TDur = 300000; Save(dir + "/3_timer.png", S, st, t, work, 1);
            S.Mode = 0; st.Popup = 2; Save(dir + "/4_msgs.png", S, st, t, work, 0);
            st.Popup = 0; S.Mode = 0;
            st.Hover = false; st.Popup = 0; st.BannerLabel = "선생님"; st.BannerText = "하던 일을 멈추고 바로 앉아 선생님을 봅니다."; st.BannerUntil = t.AddSeconds(10);
            Save(dir + "/5_banner.png", S, st, t, work, 1);
            var pic = new Bitmap(240, 160);
            using (var g = Graphics.FromImage(pic))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.FromArgb(255, 236, 179));
                g.FillEllipse(Brushes.White, 40, 40, 70, 80); g.FillEllipse(Brushes.White, 130, 40, 70, 80);
                g.FillEllipse(Brushes.Black, 62, 70, 30, 34); g.FillEllipse(Brushes.Black, 152, 70, 30, 34);
            }
            st.BannerImage = pic; st.BannerLabel = "선생님을 보세요"; st.BannerText = "";
            Save(dir + "/5b_banner_image.png", S, st, t, work, 1);
            st.BannerImage = null;
            st.BannerLabel = null; S.Readable = true; Save(dir + "/6_readable.png", S, st, t.AddHours(2.9), work, 1);
            S.Readable = false; S.Color = "#FFC61A"; S.Mode = 2; st.WBase = 65430; st.Laps.Add(30120); Save(dir + "/7_yellow_sw.png", S, st, t, work, 2);
            S.Mode = 0; S.Color = "#1F5FFF"; S.Scale = 0.55f; Save(dir + "/8_small.png", S, st, t.AddHours(1), work, 0);
            S.Scale = 1f; S.ShowSeconds = false; Save(dir + "/9_nosec.png", S, st, t, work, 0);
            S.ShowSeconds = true; S.DigitFont = true; st.Hover = false;
            Save(dir + "/10_font_a.png", S, st, new DateTime(2026, 10, 8, 11, 11, 11), work, 0);
            Save(dir + "/10_font_b.png", S, st, new DateTime(2026, 10, 8, 10, 48, 08), work, 0);
            testTop = 70; st.Hover = true; st.Popup = 0; S.Mode = 1; S.DigitFont = false; S.Memo = "오늘 준비물: 색연필";
            Save(dir + "/12_layout.png", S, st, t, new Rectangle(0, 0, 900, 700), 1);
            testTop = 30; S.Mode = 0; S.Memo = "";
            var S5 = Settings.Defaults(); S5.FontName = "★ 주아"; S5.ClockFontName = "★ 검은고딕"; S5.DigitFont = true; S5.MemoFont = "★ 개구 손글씨";
            S5.Memo = "오늘 준비물: 색연필, 가위\n5교시 강당 이동"; S5.MemoBgOn = true; S5.MemoBgColor = "#FFD3E0";
            var st5 = new HudState { Hover = true };
            Save(dir + "/11_builtin_hover.png", S5, st5, t, work, 1);
        }

        public static void Log(Exception ex)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Settings.Dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Dir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (ex == null ? "unknown" : ex.ToString()) + "\r\n\r\n");
            }
            catch { }
        }

        static Settings Sample()
        {
            var S = Settings.Defaults();
            string[] names = { "김하늘", "이도윤", "박서연", "최지호", "정예린", "강민준", "조아인", "윤시우", "장하은", "임도현" };
            for (int i = 0; i < names.Length; i++) S.Students.Add(new Student(i + 1, names[i]) { Gender = i % 2 == 0 ? "여" : "남" });
            S.Picked.AddRange(new[] { 4, 9 });
            var wp = new WeekPlan();
            string[,] t = {
                {"국어|시를 읽고 장면 떠올리기","수학|분수의 덧셈","사회|우리 지역의 문화유산","과학|식물의 한살이","체육|피구 규칙 알기"},
                {"수학|분수의 뺄셈","국어|인물의 마음 짐작하기","음악|리코더 연주","미술|가을 풍경 그리기","창체|안전 교육"},
                {"국어|글의 짜임 알기","수학|단원 평가","영어|Lesson 7 What time is it?","도덕|약속의 소중함","체육|줄넘기"},
                {"수학|문제 해결","과학|씨앗 관찰하기","국어|독서 감상문 쓰기","사회|문화유산 조사 발표","창체|동아리"},
                {"국어|받아쓰기","수학|놀이 수학","영어|단어 퀴즈","미술|작품 감상","자율|학급 회의"} };
            for (int d = 0; d < 5; d++)
            {
                var l = new List<WeekCell>();
                for (int p = 0; p < 5; p++) { var a = t[d, p].Split('|'); l.Add(new WeekCell(a[0], a[1])); }
                wp.Days[(d + 1).ToString()] = l;
            }
            wp.Days["3"][4].Move = true;
            S.WeekPlans[Settings.WeekKey(new DateTime(2026, 10, 12))] = wp;
            return S;
        }

        static void Grab(Form f, string path)
        {
            Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(f.Location, Point.Empty, f.Size);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void Shots(string dir)
        {
            Application.EnableVisualStyles();
            var S = Sample();
            // 테마별 위젯 모양
            int ti = 0;
            foreach (var th in ThemePreset.All)
            {
                var s2 = Settings.Defaults(); th.Apply(s2);
                Save(dir + "/theme_" + (ti++) + ".png", s2, new HudState(), new DateTime(2026, 10, 8, 9, 45, 52), new Rectangle(0, 0, 900, 640), th.BgStyle == 0 && th.Color == "#FFFFFF" ? 1 : (ti % 2 == 0 ? 0 : 1));
            }
            var sf = new SettingsForm(S.Clone()) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) }; if (Environment.GetEnvironmentVariable("TALL") == "1") sf.Height = 1900;
            int step = 0;
            var t = new System.Windows.Forms.Timer { Interval = 700 };
            t.Tick += (o, e) =>
            {
                if (step % 2 == 0) { int i = step / 2; if (i >= sf.TabCount) { t.Stop(); sf.Close(); return; } sf.SelectTab(i); if (i == 2 && Environment.GetEnvironmentVariable("WEEKTAB") == "1") sf.ShowWeekSubTab(); }
                else Grab(sf, dir + "/settings_" + (step / 2 + 1) + ".png");
                step++;
            };
            sf.Shown += (o, e) => t.Start();
            Application.Run(sf);

            var wf = new WeekForm(S, null, new DateTime(2026, 10, 14)) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
            var t2 = new System.Windows.Forms.Timer { Interval = 900 };
            t2.Tick += (o, e) => { t2.Stop(); Grab(wf, dir + "/week.png"); wf.Close(); };
            wf.Shown += (o, e) => t2.Start();
            Application.Run(wf);

            var Sb = Sample(); var bds = new[] { "10-03", "10-15", "10-15", "10-28", "11-02", "03-05" };
            for (int i = 0; i < bds.Length && i < Sb.Students.Count; i++) Sb.Students[i].Birthday = bds[i];
            var bf = new BirthdayListForm(Sb) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
            var tb = new System.Windows.Forms.Timer { Interval = 900 };
            tb.Tick += (o, e) => { tb.Stop(); Grab(bf, dir + "/bday.png"); bf.Close(); };
            bf.Shown += (o, e) => tb.Start();
            Application.Run(bf);
            if (Environment.GetEnvironmentVariable("ONLYBDAY") == "1") return;

            var S3 = Sample(); S3.Memo = "오늘 준비물: 색연필, 가위\n5교시 강당 이동";
            var mf = new MemoForm(S3) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
            var t5 = new System.Windows.Forms.Timer { Interval = 900 };
            t5.Tick += (o, e) => { t5.Stop(); Grab(mf, dir + "/memo.png"); mf.Close(); };
            mf.Shown += (o, e) => t5.Start();
            Application.Run(mf);

            var S4 = Sample(); S4.Memo = "오늘 준비물: 색연필, 가위\n5교시 강당 이동";
            var me = new MemoEditor(S4, 1f);
            me.Place(new Rectangle(40, 40, 420, 150), 60);
            var t6 = new System.Windows.Forms.Timer { Interval = 900 };
            t6.Tick += (o, e) => { t6.Stop(); using (var bmp = new Bitmap(520, 300)) { using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(0, 0, 0, 0, bmp.Size); bmp.Save(dir + "/memo_inline.png", ImageFormat.Png); } me.Close(); };
            me.Shown += (o, e) => t6.Start();
            Application.Run(me);

            var cpf = new ColorPicker(Color.FromArgb(255, 138, 31)) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
            var t4 = new System.Windows.Forms.Timer { Interval = 900 };
            t4.Tick += (o, e) => { t4.Stop(); Grab(cpf, dir + "/colorpicker.png"); cpf.Close(); };
            cpf.Shown += (o, e) => t4.Start();
            Application.Run(cpf);

            var pf = new PickerForm(S, null, 1f, new Rectangle(0, 0, 1380, 880));
            int ps = 0;
            var t3 = new System.Windows.Forms.Timer { Interval = 100 };
            t3.Tick += (o, e) =>
            {
                ps++;
                if (ps == 6) Grab(pf, dir + "/pick_grid.png");
                if (ps == 7) pf.DrawNow();
                if (ps == 18) Grab(pf, dir + "/pick_spin.png");
                if (ps == 40) Grab(pf, dir + "/pick_reveal.png");
                if (ps == 42) { t3.Stop(); pf.Close(); }
            };
            pf.Shown += (o, e) => t3.Start();
            Application.Run(pf);
        }

        static int testTop = 30;
        static void Save(string path, Settings S, HudState st, DateTime t, Rectangle work, int bg)
        {
            var c = Painter.Compose(S, st, t, 1f, 450, testTop, work);
            using (var canvas = new Bitmap(900, Math.Max(c.Win.Bottom + 30, 260)))
            using (var g = Graphics.FromImage(canvas))
            {
                if (bg == 0) g.Clear(Color.FromArgb(246, 247, 250));
                else if (bg == 1) using (var b = new LinearGradientBrush(new Rectangle(0, 0, 900, canvas.Height), Color.FromArgb(120, 170, 120), Color.FromArgb(70, 110, 160), 30f)) g.FillRectangle(b, 0, 0, 900, canvas.Height);
                else g.Clear(Color.FromArgb(28, 52, 44));
                g.DrawImage(c.Bmp, c.Win.Location);
                canvas.Save(path, ImageFormat.Png);
            }
            c.Bmp.Dispose();
        }
    }
}
