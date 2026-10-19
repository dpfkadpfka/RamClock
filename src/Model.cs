using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ClassroomHud
{
    public class Period
    {
        public string Type { get; set; }   // class | break | lunch
        public string Start { get; set; }
        public string End { get; set; }
        public Period() { }
        public Period(string t, string s, string e) { Type = t; Start = s; End = e; }
        public Period Clone() { return new Period(Type, Start, End); }
    }

    public class Subj
    {
        public string Name { get; set; }
        public bool Move { get; set; }
        public Subj() { }
        public Subj(string n, bool m) { Name = n; Move = m; }
    }

    public class Rule
    {
        public bool On { get; set; }
        public int Times { get; set; }
        public string Text { get; set; }
        public string Sound { get; set; }
        public Rule() { }
        public Rule(bool on, int times, string text) { On = on; Times = times; Text = text; }
    }

    public class QuickLink
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public QuickLink() { }
        public QuickLink(string n, string u) { Name = n; Url = u; }
    }

    public class Msg
    {
        public string Label { get; set; }
        public string Text { get; set; }
        public string Sound { get; set; }
        public string Image { get; set; }
        public Msg() { }
        public Msg(string l, string t) { Label = l; Text = t; }
    }

    public class Student
    {
        public int No { get; set; }
        public string Name { get; set; }
        public string Photo { get; set; }
        public string Gender { get; set; }   // "남", "여", 또는 빈칸
        public Student() { }
        public Student(int no, string name) { No = no; Name = name; }
    }

    public class WeekCell
    {
        public string Subj { get; set; }
        public string Content { get; set; }
        public bool Move { get; set; }
        public WeekCell() { }
        public WeekCell(string s, string c) { Subj = s; Content = c; }
    }

    public class WeekPlan
    {
        public string Pdf { get; set; }                       // media 폴더의 원본 PDF
        public Dictionary<string, List<WeekCell>> Days { get; set; }   // "1"(월)~"5"(금), 교시 순서
        public WeekPlan() { Days = new Dictionary<string, List<WeekCell>>(); }
    }

    public class Settings
    {
        public int Mode { get; set; }                 // 0 시간표, 1 타이머, 2 스톱워치
        public string Color { get; set; } = "#1F5FFF";
        public float Scale { get; set; } = 1f;
        public int X { get; set; } = int.MinValue;     // 위젯 가운데 X (화면 좌표)
        public int Y { get; set; } = int.MinValue;     // 위젯 위쪽 Y
        public bool Readable { get; set; }
        public bool Voice { get; set; } = true;
        public string VoiceName { get; set; } = "";      // 비면 한국어 기본 목소리
        public int VoiceRate { get; set; } = 0;          // -10(느리게) ~ 10(빠르게)
        public int VoiceVolume { get; set; } = 100;
        public bool Sfx { get; set; } = true;
        public bool Locked { get; set; }
        public bool ShowDate { get; set; } = true;
        public bool ShowSeconds { get; set; } = true;
        public bool PickResetOnStart { get; set; } = true;
        public bool ShowStatus { get; set; } = true;
        public bool TopMostOn { get; set; } = true;
        public bool AutoStart { get; set; }
        public string FontName { get; set; } = "Malgun Gothic";
        public bool DigitFont { get; set; }
        public int BannerSeconds { get; set; } = 10;
        public bool Outline { get; set; } = true;
        public bool Shadow { get; set; } = true;
        public string SubColor { get; set; } = "";          // 날짜·안내 글자 색 (비우면 글자 색과 같게)
        public string FontFile { get; set; } = "";          // 불러온 글꼴 파일(media)
        public bool Bold { get; set; } = true;
        public string OutlineColor { get; set; } = "#FFFFFF";
        public int OutlineWidth { get; set; } = 4;
        public bool Glow { get; set; }
        public int BgStyle { get; set; }                    // 0 투명, 1 단색, 2 그라데이션, 3 이미지
        public string BgColor { get; set; } = "#FFFFFF";
        public string BgColor2 { get; set; } = "#DDE7FF";
        public int BgOpacity { get; set; } = 85;
        public string BgImage { get; set; } = "";
        public int Radius { get; set; } = 18;
        public bool BgBorder { get; set; }
        public string BgBorderColor { get; set; } = "#FFFFFF";
        public string Theme { get; set; } = "선명한 파랑";
        public string Memo { get; set; } = "";
        public string MemoColor { get; set; } = "#3A2E1F";
        public bool MemoBgOn { get; set; } = true;
        public string MemoBgColor { get; set; } = "#FFF3BF";
        public int MemoBgOpacity { get; set; } = 92;
        public string MemoFont { get; set; } = "";
        public int MemoSize { get; set; } = 19;
        public bool MemoBold { get; set; } = true;
        public bool MemoOutline { get; set; }
        public string MemoOutlineColor { get; set; } = "#FFFFFF";
        public string ClockFontName { get; set; } = "";     // 비우면 글자 글꼴과 같게
        public string ClockFontFile { get; set; } = "";
        public bool ClockBold { get; set; } = true;
        public bool? ClockOutlineSet { get; set; }          // 예전 설정 옮기기용
        public bool ClockOutline { get; set; } = true;
        public string ClockOutlineColor { get; set; } = "#FFFFFF";
        public int ClockOutlineWidth { get; set; } = 5;
        public int StudentCount { get; set; } = 25;
        public List<Student> Students { get; set; }
        public int PickMode { get; set; }          // 0 번호+이름, 1 이름만, 2 번호만
        public bool PickNoRepeat { get; set; } = true;
        public bool PickEffect { get; set; } = true;
        public bool PickSound { get; set; } = true;
        public List<int> Picked { get; set; }
        public Dictionary<string, WeekPlan> WeekPlans { get; set; }
        public List<int> Presets { get; set; }
        public List<Period> Schedule { get; set; }
        public List<int> OverrideDays { get; set; }
        public Dictionary<string, List<Period>> DaySchedules { get; set; }
        public Dictionary<string, List<Subj>> Timetable { get; set; }
        public Dictionary<string, Rule> Rules { get; set; }
        public List<Msg> Messages { get; set; }
        public List<QuickLink> Links { get; set; }
        public List<string> Tools { get; set; }       // 시계 아래 [교실 도구]에 보일 버튼
        public static readonly string[][] ToolList = { new[] { "msgs", "선생님 안내" }, new[] { "pick", "뽑기" }, new[] { "memo", "텍스트" }, new[] { "week", "이번 주" } };
        public static List<QuickLink> DefaultLinks()
        {
            return new List<QuickLink> { new QuickLink("아이스크림", "https://www.i-scream.co.kr"), new QuickLink("유튜브", "https://www.youtube.com") };
        }

        public static readonly string[] RuleKeys = { "preStart", "start", "move5", "move3" };
        public static readonly string[] RuleNames = { "수업 시작 1분 전", "수업 시작", "이동 수업 5분 전", "이동 수업 3분 전" };

        public static List<Period> DefaultSchedule()
        {
            return new List<Period> {
                new Period("class","09:00","09:40"), new Period("break","09:40","09:50"),
                new Period("class","09:50","10:30"), new Period("break","10:30","10:40"),
                new Period("class","10:40","11:20"), new Period("break","11:20","11:30"),
                new Period("class","11:30","12:10"), new Period("lunch","12:10","13:00"),
                new Period("class","13:00","13:40")
            };
        }

        static List<Subj> L(params string[] n)
        {
            var l = new List<Subj>();
            foreach (var x in n) l.Add(new Subj(x, false));
            return l;
        }

        public static Dictionary<string, Rule> DefaultRules()
        {
            return new Dictionary<string, Rule> {
                { "preStart", new Rule(true, 2, "곧 {n}교시 {subj} 수업이 시작됩니다. 자리에 앉아 준비해 주세요.") },
                { "start",    new Rule(true, 1, "{n}교시 {subj} 수업을 시작합니다.") },
                { "move5",    new Rule(true, 2, "{n}교시는 이동 수업입니다. 5분 뒤 수업이 시작되니 준비물을 챙겨 이동해 주세요.") },
                { "move3",    new Rule(true, 1, "이동 수업 시작 3분 전입니다. 지금 이동해 주세요.") }
            };
        }

        public static List<Msg> DefaultMessages()
        {
            return new List<Msg> {
                new Msg("선생님", "하던 일을 멈추고 바로 앉아 선생님을 봅니다."),
                new Msg("조용히", "말하지 않습니다. 조용히 집중합니다."),
                new Msg("집중", "지금 하는 활동에 집중합니다."),
                new Msg("하던 일 멈추기", "하던 일을 멈추고 손을 내려놓습니다."),
                new Msg("질문 있어요", "질문이 있으면 조용히 손을 듭니다."),
                new Msg("준비 완료!", "수업 준비를 마쳤는지 확인합니다. 준비되었으면 바른 자세로 앉습니다.")
            };
        }

        public static Settings Defaults()
        {
            var s = new Settings();
            s.FillMissing();
            return s;
        }

        public void FillMissing()
        {
            if (Presets == null || Presets.Count == 0) Presets = new List<int> { 3, 5, 10 };
            if (Schedule == null || Schedule.Count == 0) Schedule = DefaultSchedule();
            if (OverrideDays == null) OverrideDays = new List<int>();
            if (DaySchedules == null) DaySchedules = new Dictionary<string, List<Period>>();
            if (Timetable == null)
            {
                Timetable = new Dictionary<string, List<Subj>> {
                    { "1", L("국어","수학","사회","과학","체육") },
                    { "2", L("수학","국어","음악","미술","창체") },
                    { "3", L("국어","수학","영어","도덕","체육") },
                    { "4", L("수학","과학","국어","사회","창체") },
                    { "5", L("국어","수학","영어","미술","자율") }
                };
            }
            for (int d = 1; d <= 5; d++) if (!Timetable.ContainsKey(d.ToString())) Timetable[d.ToString()] = new List<Subj>();
            var dr = DefaultRules();
            if (Rules == null) Rules = dr;
            foreach (var k in RuleKeys) if (!Rules.ContainsKey(k) || Rules[k] == null) Rules[k] = dr[k];
            if (Messages == null) Messages = DefaultMessages();
            if (Links == null) Links = DefaultLinks();
            if (Tools == null) Tools = new List<string> { "pick", "memo" };   // 처음에는 뽑기·텍스트만
            if (Scale <= 0.1f || float.IsNaN(Scale)) Scale = 1f;
            if (Mode < 0 || Mode > 2) Mode = 0;
            if (string.IsNullOrEmpty(Color)) Color = "#1F5FFF";
            if (string.IsNullOrEmpty(FontName)) FontName = "Malgun Gothic";
            if (BannerSeconds < 0 || BannerSeconds > 600) BannerSeconds = 10;
            if (StudentCount < 1 || StudentCount > 60) StudentCount = 25;
            if (Students == null) Students = new List<Student>();
            if (Picked == null) Picked = new List<int>();
            if (WeekPlans == null) WeekPlans = new Dictionary<string, WeekPlan>();
            if (PickMode < 0 || PickMode > 2) PickMode = 0;
            if (Readable && BgStyle == 0) { BgStyle = 1; BgColor = "#FFFFFF"; BgOpacity = 85; }
            Readable = false;
            if (BgStyle < 0 || BgStyle > 3) BgStyle = 0;
            BgOpacity = Math.Max(0, Math.Min(100, BgOpacity));
            Radius = Math.Max(0, Math.Min(60, Radius));
            OutlineWidth = Math.Max(1, Math.Min(12, OutlineWidth));
            if (string.IsNullOrEmpty(OutlineColor)) OutlineColor = "#FFFFFF";
            if (string.IsNullOrEmpty(BgColor)) BgColor = "#FFFFFF";
            if (string.IsNullOrEmpty(BgColor2)) BgColor2 = "#DDE7FF";
            if (string.IsNullOrEmpty(BgBorderColor)) BgBorderColor = "#FFFFFF";
            if (SubColor == null) SubColor = "";
            if (FontFile == null) FontFile = "";
            if (Memo == null) Memo = "";
            if (string.IsNullOrEmpty(MemoColor)) MemoColor = "#3A2E1F";
            if (string.IsNullOrEmpty(MemoBgColor)) MemoBgColor = "#FFF3BF";
            if (string.IsNullOrEmpty(MemoOutlineColor)) MemoOutlineColor = "#FFFFFF";
            if (MemoFont == null) MemoFont = "";
            MemoSize = Math.Max(10, Math.Min(48, MemoSize == 0 ? 19 : MemoSize));
            if (MemoBgOpacity <= 0) MemoBgOpacity = 92;
            if (ClockFontName == null) ClockFontName = "";
            if (ClockFontFile == null) ClockFontFile = "";
            if (ClockOutlineSet == null) { ClockOutline = Outline; ClockOutlineColor = OutlineColor; ClockOutlineWidth = Math.Min(12, OutlineWidth + 1); ClockOutlineSet = true; }
            if (string.IsNullOrEmpty(ClockOutlineColor)) ClockOutlineColor = "#FFFFFF";
            ClockOutlineWidth = Math.Max(1, Math.Min(12, ClockOutlineWidth));
        }

        public List<Period> ActiveSchedule(int dow)
        {
            List<Period> l;
            if (OverrideDays.Contains(dow) && DaySchedules.TryGetValue(dow.ToString(), out l) && l != null) return l;
            return Schedule;
        }

        public static string WeekKey(DateTime d)
        {
            int diff = ((int)d.DayOfWeek + 6) % 7;   // 월요일=0
            return d.Date.AddDays(-diff).ToString("yyyy-MM-dd");
        }

        public WeekCell WeekCellOf(DateTime date, int n)
        {
            WeekPlan wp; List<WeekCell> l;
            int dow = (int)date.DayOfWeek;
            if (n >= 1 && WeekPlans.TryGetValue(WeekKey(date), out wp) && wp != null && wp.Days != null &&
                wp.Days.TryGetValue(dow.ToString(), out l) && l != null && n - 1 < l.Count && l[n - 1] != null &&
                !string.IsNullOrWhiteSpace(l[n - 1].Subj)) return l[n - 1];
            return null;
        }

        public Subj SubjectOn(DateTime date, int n)
        {
            var wc = WeekCellOf(date, n);
            if (wc != null) return new Subj(wc.Subj.Trim(), wc.Move);
            return SubjectOf((int)date.DayOfWeek, n);
        }

        public Student StudentOf(int no)
        {
            foreach (var st in Students) if (st != null && st.No == no) return st;
            return null;
        }

        public Subj SubjectOf(int dow, int n)
        {
            List<Subj> l;
            if (n >= 1 && Timetable.TryGetValue(dow.ToString(), out l) && l != null && n - 1 < l.Count && l[n - 1] != null) return l[n - 1];
            return new Subj("", false);
        }

        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        public static string Dir
        {
            get
            {
                string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(app, "RamClock"), old = Path.Combine(app, "ClassroomHUD-Yeram");
                // 예전 이름(교실 HUD)으로 저장된 설정이 있으면 그대로 옮겨 오기
                if (!Directory.Exists(dir) && Directory.Exists(old))
                {
                    try { CopyDir(old, dir); } catch { }
                }
                return dir;
            }
        }
        public static string MediaDir { get { return Path.Combine(Dir, "media"); } }
        public static string MediaPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string p = Path.Combine(MediaDir, name);
            return File.Exists(p) ? p : null;
        }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var js = new JavaScriptSerializer();
                    var s = js.Deserialize<Settings>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (s != null) { s.FillMissing(); return s; }
                }
            }
            catch { }
            return Defaults();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var js = new JavaScriptSerializer();
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, js.Serialize(this), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { }
        }

        public Settings Clone()
        {
            var js = new JavaScriptSerializer();
            var s = js.Deserialize<Settings>(js.Serialize(this));
            s.FillMissing();
            return s;
        }
    }

    // 한 번에 바꾸는 꾸미기 묶음
    public class ThemePreset
    {
        public string Name, Color, Sub, OutlineColor, Bg1, Bg2, Border;
        public bool DigitFont, Outline, Shadow, Glow, BgBorder;
        public int OutlineWidth = 4, BgStyle, Opacity = 85, Radius = 18;

        public static readonly ThemePreset[] All = {
            new ThemePreset { Name = "선명한 파랑", Color = "#1F5FFF", Sub = "", Outline = true, OutlineColor = "#FFFFFF", Shadow = true },
            new ThemePreset { Name = "미니멀 화이트", Color = "#FFFFFF", Sub = "", DigitFont = true, Shadow = true, OutlineColor = "#1A2233" },
            new ThemePreset { Name = "유리 카드", Color = "#FFFFFF", Sub = "", DigitFont = true, BgStyle = 1, Bg1 = "#0F172A", Opacity = 62, Radius = 24, BgBorder = true, Border = "#FFFFFF" },
            new ThemePreset { Name = "칠판", Color = "#FFF7DA", Sub = "#CBE6C1", DigitFont = true, Shadow = true, BgStyle = 2, Bg1 = "#2F5D46", Bg2 = "#1D3D2E", Opacity = 96, Radius = 12, BgBorder = true, Border = "#A07A45" },
            new ThemePreset { Name = "파스텔", Color = "#3D3A8C", Sub = "#8A6FB8", DigitFont = true, BgStyle = 2, Bg1 = "#FFE1EC", Bg2 = "#DDEBFF", Opacity = 95, Radius = 28 },
            new ThemePreset { Name = "네온", Color = "#3EF2FF", Sub = "#FF5FD8", Glow = true, BgStyle = 1, Bg1 = "#070B18", Opacity = 88, Radius = 18, BgBorder = true, Border = "#3EF2FF" },
            new ThemePreset { Name = "종이 메모", Color = "#3A2E1F", Sub = "", DigitFont = true, BgStyle = 1, Bg1 = "#FFF3BF", Opacity = 97, Radius = 6 },
            new ThemePreset { Name = "노랑 디지털", Color = "#FFC61A", Sub = "", Outline = true, OutlineColor = "#1A1A1A", OutlineWidth = 5, Shadow = true }
        };

        public void Apply(Settings s)
        {
            s.Theme = Name; s.Color = Color; s.SubColor = Sub ?? ""; s.DigitFont = DigitFont;
            s.Outline = Outline; s.OutlineColor = OutlineColor ?? "#FFFFFF"; s.OutlineWidth = OutlineWidth;
            s.ClockOutline = Outline; s.ClockOutlineColor = OutlineColor ?? "#FFFFFF"; s.ClockOutlineWidth = Math.Min(12, OutlineWidth + 1); s.ClockOutlineSet = true;
            s.Shadow = Shadow; s.Glow = Glow; s.BgStyle = BgStyle;
            if (Bg1 != null) s.BgColor = Bg1;
            s.BgColor2 = Bg2 ?? s.BgColor;
            s.BgOpacity = Opacity; s.Radius = Radius; s.BgBorder = BgBorder;
            if (Border != null) s.BgBorderColor = Border;
        }
    }

    public class Slot
    {
        public string Type;
        public int StartMin, EndMin, N;
    }

    public class Ev
    {
        public string Id; public int T; public string Text; public int Times; public string Sound;
    }

    public static class Sched
    {
        public static int ToMin(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            s = s.Trim().Replace('：', ':').Replace('.', ':').Replace(" ", "");
            bool pm = s.StartsWith("오후"), am = s.StartsWith("오전");
            if (pm || am) s = s.Substring(2);
            s = s.Replace("시", ":").Replace("분", "").TrimEnd(':');
            // 숫자만 쓴 경우: 9 → 9:00, 945 → 9:45, 0945 → 09:45
            if (s.Length > 0 && s.Length <= 4 && System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d+$"))
                s = s.Length <= 2 ? s + ":00" : s.Substring(0, s.Length - 2) + ":" + s.Substring(s.Length - 2);
            var p = s.Split(':');
            int h, m;
            if (p.Length == 2 && p[1].Length == 0) p[1] = "0";
            if (p.Length == 2 && int.TryParse(p[0], out h) && pm && h < 12) p[0] = (h + 12).ToString();
            if (p.Length != 2 || !int.TryParse(p[0], out h) || !int.TryParse(p[1], out m)) return -1;
            if (h < 0 || h > 23 || m < 0 || m > 59) return -1;
            return h * 60 + m;
        }

        public static string FromMin(int m)
        {
            m = Math.Max(0, Math.Min(1439, m));
            return (m / 60).ToString("00") + ":" + (m % 60).ToString("00");
        }

        public static List<Slot> Slots(Settings S, int dow)
        {
            var list = new List<Slot>();
            foreach (var p in S.ActiveSchedule(dow))
            {
                if (p == null) continue;
                int a = ToMin(p.Start), b = ToMin(p.End);
                if (a < 0 || b <= a) continue;
                list.Add(new Slot { Type = p.Type ?? "class", StartMin = a, EndMin = b });
            }
            list.Sort((x, y) => x.StartMin.CompareTo(y.StartMin));
            int n = 0;
            foreach (var s in list) if (s.Type == "class") s.N = ++n;
            return list;
        }

        public static string Status(Settings S, DateTime now)
        {
            int dow = (int)now.DayOfWeek;
            if (dow == 0 || dow == 6) return "즐거운 주말이에요!";
            var sl = Slots(S, dow);
            if (sl.Count == 0) return "";
            int sec = now.Hour * 3600 + now.Minute * 60 + now.Second;
            foreach (var s in sl)
            {
                int st = s.StartMin * 60, en = s.EndMin * 60;
                if (sec >= st && sec < en)
                {
                    int left = (en - sec + 59) / 60;
                    if (s.Type == "class")
                    {
                        var t = S.SubjectOn(now, s.N);
                        string r = s.N + "교시";
                        if (!string.IsNullOrWhiteSpace(t.Name)) r += " " + t.Name.Trim();
                        if (t.Move) r += " (이동 수업)";
                        return r;
                    }
                    if (s.Type == "lunch") return "점심시간, " + left + "분 남았습니다";
                    return "쉬는 시간, " + left + "분 남았습니다";
                }
            }
            int first = sl[0].StartMin * 60, last = sl[sl.Count - 1].EndMin * 60;
            if (sec < first) return "수업 시작까지 " + ((first - sec + 59) / 60) + "분";
            if (sec >= last) return "오늘 수업이 모두 끝났어요";
            foreach (var s in sl) if (s.StartMin * 60 > sec) return "다음 시간까지 " + ((s.StartMin * 60 - sec + 59) / 60) + "분";
            return "";
        }

        public static string Fill(string text, int n, string subj)
        {
            return (text ?? "").Replace("{n}", n.ToString()).Replace("{subj}", subj ?? "").Replace("  ", " ").Trim();
        }

        public static List<Ev> DayEvents(Settings S, DateTime date)
        {
            int dow = (int)date.DayOfWeek;
            var ev = new List<Ev>();
            var R = S.Rules;
            foreach (var s in Slots(S, dow))
            {
                if (s.Type != "class") continue;
                int st = s.StartMin * 60;
                var t = S.SubjectOn(date, s.N);
                string subj = (t.Name ?? "").Trim();
                Rule r;
                r = R["preStart"]; if (r.On) ev.Add(new Ev { Id = "p" + s.N, T = st - 60, Text = Fill(r.Text, s.N, subj), Times = r.Times, Sound = r.Sound });
                r = R["start"]; if (r.On) ev.Add(new Ev { Id = "s" + s.N, T = st, Text = Fill(r.Text, s.N, subj), Times = r.Times, Sound = r.Sound });
                if (t.Move)
                {
                    r = R["move5"]; if (r.On) ev.Add(new Ev { Id = "a" + s.N, T = st - 300, Text = Fill(r.Text, s.N, subj), Times = r.Times, Sound = r.Sound });
                    r = R["move3"]; if (r.On) ev.Add(new Ev { Id = "b" + s.N, T = st - 180, Text = Fill(r.Text, s.N, subj), Times = r.Times, Sound = r.Sound });
                }
            }
            return ev;
        }
    }
}
