using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ClassroomHud
{
    // 생일 명단: 엑셀·CSV·한글·글 파일 어디에 있든 번호/이름/생일을 알아서 찾아 읽는다
    public static class Birthdays
    {
        public class Row { public int No; public string Name = ""; public string MD; }

        // "2019.03.05", "19.3.5", "190305", "20190305", "3월 5일", "3/5", "03-05", 엑셀 날짜 숫자 → "MM-dd"
        public static string ParseMD(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return null;
            Match m;
            m = Regex.Match(s, @"(\d{1,2})\s*월\s*(\d{1,2})\s*일");
            if (m.Success) return MD(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            m = Regex.Match(s, @"^(\d{2,4})\s*[.\-/년]\s*(\d{1,2})\s*[.\-/월]\s*(\d{1,2})");
            if (m.Success) return MD(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            m = Regex.Match(s, @"^(\d{4})(\d{2})(\d{2})$");
            if (m.Success) return MD(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            m = Regex.Match(s, @"^(\d{2})(\d{2})(\d{2})(-?\d{0,7})?$");   // 190305 (주민번호 앞자리 모양)
            if (m.Success && s.Length >= 6 && s.Length <= 14) return MD(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            m = Regex.Match(s, @"^(\d{1,2})\s*[.\-/]\s*(\d{1,2})\.?$");
            if (m.Success) return MD(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            double serial;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out serial) && serial > 30000 && serial < 60000)
            {
                try { var d = DateTime.FromOADate(serial); return MD(d.Month, d.Day); } catch { }
            }
            return null;
        }

        static string MD(int mo, int d)
        {
            if (mo < 1 || mo > 12 || d < 1 || d > 31) return null;
            if (d > DateTime.DaysInMonth(2024, mo)) return null;
            return mo.ToString("00") + "-" + d.ToString("00");
        }

        public static string Show(string md)
        {
            if (string.IsNullOrEmpty(md) || md.Length != 5) return "";
            return int.Parse(md.Substring(0, 2)) + "월 " + int.Parse(md.Substring(3, 2)) + "일";
        }

        static string Clean(string s) { return Regex.Replace(s ?? "", @"\s", ""); }

        public static List<Row> Read(string path, out string msg)
        {
            msg = null;
            var best = new List<Row>();
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext == ".txt")
                {
                    best = FromText(File.ReadAllText(path, DetectEnc(path)));
                }
                else
                {
                    foreach (var g in RosterImport.Grids(path, out msg))
                    {
                        var r = FromGrid(g);
                        if (r.Count > best.Count) best = r;
                    }
                }
            }
            catch (Exception ex) { msg = "파일을 읽지 못했어요. (" + ex.Message + ")"; }
            if (best.Count == 0 && msg == null) msg = "파일에서 생일을 찾지 못했어요. 이름과 생일(예: 2019.03.05 또는 3월 5일)이 같은 줄에 있으면 읽을 수 있어요.";
            return best;
        }

        static Encoding DetectEnc(string path)
        {
            var b = File.ReadAllBytes(path);
            try { new UTF8Encoding(false, true).GetString(b); return Encoding.UTF8; } catch { return Encoding.GetEncoding(949); }
        }

        // 붙여 넣은 글이나 .txt: 줄마다 번호·이름·생일
        public static List<Row> FromText(string text)
        {
            var list = new List<Row>();
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim(); if (line.Length == 0) continue;
                string md = null;
                foreach (Match dm in Regex.Matches(line, @"\d{1,2}\s*월\s*\d{1,2}\s*일|\d{2,4}\s*[.\-/]\s*\d{1,2}\s*[.\-/]\s*\d{1,2}|\b\d{6,8}\b|\b\d{1,2}\s*[/.]\s*\d{1,2}\b"))
                { md = ParseMD(dm.Value); if (md != null) { line = line.Replace(dm.Value, " "); break; } }
                if (md == null) continue;
                var r = new Row { MD = md };
                var nm = Regex.Match(line, @"[가-힣]{2,5}");
                if (nm.Success) r.Name = nm.Value;
                var no = Regex.Match(line, @"(?<!\d)(\d{1,2})(?!\d)\s*(번)?");
                if (no.Success) r.No = int.Parse(no.Groups[1].Value);
                if (r.Name.Length > 0 || r.No > 0) list.Add(r);
            }
            return list;
        }

        static string Cell(List<List<string>> g, int r, int c) { return r < g.Count && c < g[r].Count ? (g[r][c] ?? "").Trim() : ""; }

        public static List<Row> FromGrid(List<List<string>> g)
        {
            var list = new List<Row>();
            int cols = 0; foreach (var row in g) cols = Math.Max(cols, row.Count);
            int hdr = -1, cNo = -1, cName = -1, cB = -1, cMo = -1, cDay = -1;
            for (int r = 0; r < Math.Min(15, g.Count) && hdr < 0; r++)
                for (int c = 0; c < g[r].Count; c++)
                {
                    string t = Clean(Cell(g, r, c));
                    if (t.Length <= 8 && (t.Contains("생일") || t.Contains("생년월일") || t.ToLower().Contains("birth"))) { hdr = r; cB = c; }
                }
            if (hdr >= 0)
                for (int c = 0; c < g[hdr].Count; c++)
                {
                    string t = Clean(Cell(g, hdr, c));
                    if (cName < 0 && (t == "이름" || t == "성명" || t == "학생명" || t == "학생이름")) cName = c;
                    if (cNo < 0 && (t == "번호" || t == "번" || t == "출석번호" || t.ToLower() == "no")) cNo = c;
                    if (t == "월") cMo = c; if (t == "일") cDay = c;
                }
            int start = hdr + 1;
            // 제목이 없으면 내용으로 짐작: 날짜가 많은 열 = 생일, 한글 이름이 많은 열 = 이름, 1~2자리 숫자 = 번호
            for (int c = 0; c < cols; c++)
            {
                int dates = 0, kor = 0, num = 0, tot = 0;
                for (int r = start; r < g.Count; r++)
                {
                    string v = Cell(g, r, c); if (v.Length == 0) continue; tot++;
                    if (ParseMD(v) != null && !Regex.IsMatch(v, @"^\d{1,2}$")) dates++;
                    if (Regex.IsMatch(v, @"^[가-힣]{2,5}$") && RosterImport.Gender(v) == "") kor++;
                    if (Regex.IsMatch(v, @"^\d{1,2}(번)?$")) num++;
                }
                if (tot == 0) continue;
                if (cB < 0 && dates * 10 >= tot * 6) cB = c;
                else if (cName < 0 && kor * 10 >= tot * 6) cName = c;
                else if (cNo < 0 && num * 10 >= tot * 8 && c != cB) cNo = c;
            }
            if (cB < 0 && (cMo < 0 || cDay < 0)) return list;
            int auto = 1;
            for (int r = start; r < g.Count; r++)
            {
                string md = cB >= 0 ? ParseMD(Cell(g, r, cB)) : null;
                if (md == null && cMo >= 0 && cDay >= 0)
                {
                    int mo, d; if (int.TryParse(Regex.Replace(Cell(g, r, cMo), @"\D", ""), out mo) && int.TryParse(Regex.Replace(Cell(g, r, cDay), @"\D", ""), out d)) md = MD(mo, d);
                }
                if (md == null) continue;
                var row = new Row { MD = md, Name = cName >= 0 ? Cell(g, r, cName) : "" };
                if (cNo >= 0) { var m = Regex.Match(Cell(g, r, cNo), @"\d{1,2}"); if (m.Success) row.No = int.Parse(m.Value); }
                if (row.No <= 0 && cName < 0) row.No = auto;
                auto = Math.Max(auto, row.No) + 1;
                if (row.Name.Length > 0 || row.No > 0) list.Add(row);
            }
            return list;
        }

        // 오늘(주말 다음 월요일이면 지난 주말까지) 생일인 학생
        public static List<Student> Today(Settings S, DateTime now, out bool weekend)
        {
            weekend = false;
            var days = new List<DateTime> { now.Date };
            if (now.DayOfWeek == DayOfWeek.Monday) { days.Add(now.Date.AddDays(-1)); days.Add(now.Date.AddDays(-2)); weekend = true; }
            var list = new List<Student>();
            foreach (var st in S.Students)
            {
                if (st == null || string.IsNullOrEmpty(st.Birthday) || st.No > S.StudentCount) continue;
                foreach (var d in days) if (st.Birthday == d.ToString("MM-dd")) { list.Add(st); break; }
            }
            bool any = false; foreach (var st in list) if (st.Birthday != now.ToString("MM-dd")) any = true;
            weekend = weekend && any;
            return list;
        }

        public static string NameOf(Student st)
        {
            if (!string.IsNullOrWhiteSpace(st.Name)) return st.Name.Trim();
            if (!string.IsNullOrWhiteSpace(st.Nick)) return st.Nick.Trim();
            return st.No + "번";
        }
    }
}

namespace ClassroomHud
{
    // 생일 목록: 월별로, 오늘·이번 달은 눈에 띄게
    // 우리 반 생일: 달력으로 한눈에 (◀ ▶로 달 넘기기, [목록]으로 전체 목록)
    public class BirthdayListForm : GlassForm
    {
        readonly Settings S;
        readonly BirthdayCalendar cal;
        readonly System.Windows.Forms.DataGridView grid;
        readonly System.Windows.Forms.Label title;
        readonly System.Windows.Forms.Button bList;

        public BirthdayListForm(Settings s)
        {
            S = s;
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            Font = Media.UiFont(11F);
            Text = "우리 반 생일";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(940, 720);
            MinimumSize = new System.Drawing.Size(560, 480);
            TopMost = true;
            BackColor = Ui.Bg;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); } catch { }

            var top = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Top, Height = 64, Padding = new System.Windows.Forms.Padding(14, 10, 14, 6) };
            var bPrev = new System.Windows.Forms.Button { Text = "◀", Width = 48, Dock = System.Windows.Forms.DockStyle.Left };
            var bNext = new System.Windows.Forms.Button { Text = "▶", Width = 48, Dock = System.Windows.Forms.DockStyle.Left };
            var bToday = new System.Windows.Forms.Button { Text = "이번 달", Width = 90, Dock = System.Windows.Forms.DockStyle.Right };
            bList = new System.Windows.Forms.Button { Text = "목록", Width = 80, Dock = System.Windows.Forms.DockStyle.Right };
            title = new System.Windows.Forms.Label { Dock = System.Windows.Forms.DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Font = Media.UiFont(18F, true), ForeColor = Ui.Ink, Padding = new System.Windows.Forms.Padding(12, 0, 0, 0) };
            top.Controls.Add(title); top.Controls.Add(bNext); top.Controls.Add(bPrev); top.Controls.Add(bList); top.Controls.Add(new System.Windows.Forms.Panel { Width = 8, Dock = System.Windows.Forms.DockStyle.Right }); top.Controls.Add(bToday);

            cal = new BirthdayCalendar(S) { Dock = System.Windows.Forms.DockStyle.Fill };
            grid = MakeGrid(S);
            grid.Visible = false;

            bPrev.Click += (o, e) => { cal.Month = cal.Month.AddMonths(-1); Sync(); };
            bNext.Click += (o, e) => { cal.Month = cal.Month.AddMonths(1); Sync(); };
            bToday.Click += (o, e) => { cal.Month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); Sync(); };
            bList.Click += (o, e) => { grid.Visible = !grid.Visible; cal.Visible = !grid.Visible; bPrev.Enabled = bNext.Enabled = bToday.Enabled = cal.Visible; Sync(); };

            Controls.Add(cal); Controls.Add(grid); Controls.Add(top);
            Ui.Apply(this);
            Sync();
        }

        void Sync()
        {
            bList.Text = grid.Visible ? "달력" : "목록";
            title.Text = grid.Visible ? "생일 목록" : cal.Month.Year + "년 " + cal.Month.Month + "월 생일";
            cal.Invalidate();
        }

        static System.Windows.Forms.DataGridView MakeGrid(Settings S)
        {
            var g = new System.Windows.Forms.DataGridView { Dock = System.Windows.Forms.DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect };
            g.Columns.Add("d", "생일"); g.Columns.Add("no", "번호"); g.Columns.Add("nm", "이름"); g.Columns.Add("left", "남은 날");
            var today = DateTime.Today;
            var list = BirthdayCalendar.With(S);
            list.Sort((a, b) => string.Compare(a.Birthday, b.Birthday, StringComparison.Ordinal));
            foreach (var st in list)
            {
                int mo = int.Parse(st.Birthday.Substring(0, 2));
                var next = BirthdayCalendar.On(st.Birthday, today.Year); if (next < today) next = BirthdayCalendar.On(st.Birthday, today.Year + 1);
                int left = (int)(next - today).TotalDays;
                int i = g.Rows.Add(Birthdays.Show(st.Birthday), st.No + "번", Birthdays.NameOf(st), left == 0 ? "오늘!" : left + "일 뒤");
                if (left == 0) g.Rows[i].DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(255, 226, 236);
                else if (mo == today.Month) g.Rows[i].DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(232, 236, 255);
            }
            return g;
        }
    }

    public class BirthdayCalendar : System.Windows.Forms.Control
    {
        readonly Settings S;
        public DateTime Month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        readonly Dictionary<string, System.Drawing.Image> photos = new Dictionary<string, System.Drawing.Image>();

        public BirthdayCalendar(Settings s)
        {
            S = s;
            SetStyle(System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer | System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.ResizeRedraw | System.Windows.Forms.ControlStyles.UserPaint, true);
            BackColor = Ui.Bg;
        }

        public static List<Student> With(Settings S)
        {
            var list = new List<Student>();
            foreach (var st in S.Students) if (st != null && !string.IsNullOrEmpty(st.Birthday) && st.Birthday.Length >= 5 && st.No <= S.StudentCount) list.Add(st);
            return list;
        }

        public static DateTime On(string md, int year)
        {
            int mo = int.Parse(md.Substring(0, 2)), d = int.Parse(md.Substring(3, 2));
            if (mo == 2 && d == 29 && !DateTime.IsLeapYear(year)) d = 28;
            return new DateTime(year, mo, d);
        }

        System.Drawing.Image Photo(Student st)
        {
            if (string.IsNullOrEmpty(st.Photo)) return null;
            System.Drawing.Image img;
            if (photos.TryGetValue(st.Photo, out img)) return img;
            try
            {
                string p = Path.Combine(Settings.MediaDir, st.Photo);
                if (File.Exists(p)) using (var src = System.Drawing.Image.FromFile(p)) img = new System.Drawing.Bitmap(src, 96, (int)(96f * src.Height / Math.Max(1, src.Width)));
            }
            catch { img = null; }
            photos[st.Photo] = img;
            return img;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var i in photos.Values) if (i != null) i.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float k = g.DpiX / 96f;
            float pad = 14 * k, headH = 34 * k;
            var area = new System.Drawing.RectangleF(pad, 4 * k, Width - 2 * pad, Height - pad - 4 * k);
            var today = DateTime.Today;

            // 이 달 생일 모으기
            var byDay = new Dictionary<int, List<Student>>();
            foreach (var st in With(S))
            {
                if (int.Parse(st.Birthday.Substring(0, 2)) != Month.Month) continue;
                int d = On(st.Birthday, Month.Year).Day;
                if (!byDay.ContainsKey(d)) byDay[d] = new List<Student>();
                byDay[d].Add(st);
            }

            int first = (int)Month.DayOfWeek, days = DateTime.DaysInMonth(Month.Year, Month.Month);
            int rows = (first + days + 6) / 7;
            float cw = area.Width / 7f, ch = (area.Height - headH) / rows;
            string[] wd = { "일", "월", "화", "수", "목", "금", "토" };
            using (var hf = Media.UiFont(12F, true))
                for (int i = 0; i < 7; i++)
                {
                    var c = i == 0 ? System.Drawing.Color.FromArgb(226, 72, 92) : i == 6 ? System.Drawing.Color.FromArgb(64, 110, 230) : Ui.Muted;
                    using (var b = new System.Drawing.SolidBrush(c))
                        g.DrawString(wd[i], hf, b, new System.Drawing.RectangleF(area.X + i * cw, area.Y, cw, headH), new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center });
                }

            using (var df = Media.UiFont(12F, true))
            using (var nf = Media.UiFont(Math.Max(9F, Math.Min(15F, ch / k / 7.5f)), true))
            using (var tagF = Media.UiFont(9F, true))
                for (int d = 1; d <= days; d++)
                {
                    int idx = first + d - 1, col = idx % 7, row = idx / 7;
                    var r = new System.Drawing.RectangleF(area.X + col * cw + 3 * k, area.Y + headH + row * ch + 3 * k, cw - 6 * k, ch - 6 * k);
                    bool isToday = Month.Year == today.Year && Month.Month == today.Month && d == today.Day;
                    List<Student> who; byDay.TryGetValue(d, out who);
                    var fill = who != null ? System.Drawing.Color.FromArgb(255, 236, 243) : System.Drawing.Color.White;
                    using (var path = RoundRect(r, 10 * k))
                    {
                        using (var b = new System.Drawing.SolidBrush(fill)) g.FillPath(b, path);
                        using (var pen = new System.Drawing.Pen(isToday ? Ui.Accent : (who != null ? System.Drawing.Color.FromArgb(255, 160, 190) : Ui.Line), isToday ? 3 * k : 1 * k)) g.DrawPath(pen, path);
                    }
                    var dc = col == 0 ? System.Drawing.Color.FromArgb(226, 72, 92) : col == 6 ? System.Drawing.Color.FromArgb(64, 110, 230) : Ui.Ink;
                    using (var b = new System.Drawing.SolidBrush(dc)) g.DrawString(d.ToString(), df, b, r.X + 6 * k, r.Y + 4 * k);
                    if (isToday)
                        using (var b = new System.Drawing.SolidBrush(Ui.Accent)) g.DrawString("오늘", tagF, b, r.Right - 34 * k, r.Y + 6 * k);
                    if (who == null) continue;

                    // 한 명이면 사진 + 이름, 여러 명이면 이름만 줄줄이
                    float y = r.Y + 26 * k;
                    if (who.Count == 1)
                    {
                        var img = Photo(who[0]);
                        float avail = r.Bottom - y - nf.GetHeight(g) - 6 * k;
                        if (img != null && avail > 24 * k)
                        {
                            float sz = Math.Min(avail, r.Width - 16 * k);
                            var ir = new System.Drawing.RectangleF(r.X + (r.Width - sz) / 2, y, sz, sz);
                            using (var clip = new System.Drawing.Drawing2D.GraphicsPath()) { clip.AddEllipse(ir); var old = g.Clip; g.SetClip(clip); g.DrawImage(img, ir); g.Clip = old; }
                            y += sz + 3 * k;
                        }
                        else y = Math.Max(y, r.Y + (r.Height - nf.GetHeight(g)) / 2 + 6 * k);
                    }
                    foreach (var st in who)
                    {
                        if (y + nf.GetHeight(g) > r.Bottom - 2 * k) break;
                        string t = st.No + "번 " + Birthdays.NameOf(st);
                        using (var b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, 40, 90)))
                            g.DrawString(t, nf, b, new System.Drawing.RectangleF(r.X + 2 * k, y, r.Width - 4 * k, nf.GetHeight(g) + 2), new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, Trimming = System.Drawing.StringTrimming.EllipsisCharacter, FormatFlags = System.Drawing.StringFormatFlags.NoWrap });
                        y += nf.GetHeight(g) + 1 * k;
                    }
                }

            if (byDay.Count == 0)
                using (var f = Media.UiFont(11F))
                using (var b = new System.Drawing.SolidBrush(Ui.Muted))
                    g.DrawString(With(S).Count == 0 ? "아직 생일이 없어요. 설정 → 뽑기 도구 → [생일 명단 넣기]로 파일을 넣어요." : "이번 달에는 생일인 친구가 없어요.", f, b, new System.Drawing.RectangleF(area.X, area.Bottom - 26 * k, area.Width, 26 * k), new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center });
        }

        static System.Drawing.Drawing2D.GraphicsPath RoundRect(System.Drawing.RectangleF r, float rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath(); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }
}
