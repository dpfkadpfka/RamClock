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
    public class BirthdayListForm : GlassForm
    {
        public BirthdayListForm(Settings S)
        {
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            Font = Media.UiFont(11F);
            Text = "우리 반 생일";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(520, 620);
            TopMost = true;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); } catch { }
            var g = new System.Windows.Forms.DataGridView { Dock = System.Windows.Forms.DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect };
            g.Columns.Add("d", "생일"); g.Columns.Add("no", "번호"); g.Columns.Add("nm", "이름"); g.Columns.Add("left", "남은 날");
            var today = DateTime.Today;
            var list = new List<Student>();
            foreach (var st in S.Students) if (st != null && !string.IsNullOrEmpty(st.Birthday) && st.No <= S.StudentCount) list.Add(st);
            list.Sort((a, b) => string.Compare(a.Birthday, b.Birthday, StringComparison.Ordinal));
            foreach (var st in list)
            {
                int mo = int.Parse(st.Birthday.Substring(0, 2)), d = int.Parse(st.Birthday.Substring(3, 2));
                if (!DateTime.IsLeapYear(today.Year) && mo == 2 && d == 29) d = 28;
                var next = new DateTime(today.Year, mo, d); if (next < today) next = next.AddYears(1);
                int left = (int)(next - today).TotalDays;
                int i = g.Rows.Add(Birthdays.Show(st.Birthday), st.No + "번", Birthdays.NameOf(st), left == 0 ? "오늘!" : left + "일 뒤");
                if (left == 0) g.Rows[i].DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(255, 226, 236);
                else if (mo == today.Month) g.Rows[i].DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(232, 236, 255);
            }
            var note = new System.Windows.Forms.Label { Dock = System.Windows.Forms.DockStyle.Bottom, Height = 40, Text = list.Count == 0 ? "아직 생일이 없어요. 설정 → 뽑기 도구 → [생일 명단 넣기]로 넣어요." : "분홍 = 오늘, 파랑 = 이번 달 생일", TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
            Controls.Add(g); Controls.Add(note);
            Ui.Apply(this);
        }
    }
}
