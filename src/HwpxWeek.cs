using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ClassroomHud
{
    // 한글(HWPX) 주간학습안내: 표를 칸 그대로 읽어서 요일×교시로 나눈다
    public static class HwpxWeek
    {
        public static WeekParseResult Parse(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string tmp = null;
            try
            {
                if (ext == ".hwp")
                {
                    tmp = HwpToHwpx(path);
                    if (tmp == null) return new WeekParseResult { Message = "옛 한글(.hwp) 파일은 컴퓨터에 한글이 설치되어 있어야 읽을 수 있어요.\n한글에서 [다른 이름으로 저장 → HWPX 또는 PDF]로 저장해서 넣어 주세요." };
                    path = tmp;
                }
                var tables = new List<List<List<string>>>();
                var allText = new StringBuilder();
                using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var z = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    var secs = new List<string>();
                    foreach (var e in z.Entries) if (Regex.IsMatch(e.FullName, @"^Contents/section\d+\.xml$", RegexOptions.IgnoreCase)) secs.Add(e.FullName);
                    secs.Sort();
                    foreach (var s in secs)
                    {
                        var d = new XmlDocument();
                        using (var st = z.GetEntry(s).Open()) d.Load(st);
                        allText.Append(d.DocumentElement.InnerText).Append(' ');
                        foreach (XmlElement t in d.GetElementsByTagName("*"))
                            if (t.LocalName == "tbl") tables.Add(Grid(t));
                    }
                }
                if (tables.Count == 0) return new WeekParseResult { Message = "한글 파일에서 표를 찾지 못했어요." };
                WeekParseResult best = null; int bestN = -1;
                foreach (var g in tables)
                {
                    var r = FromGrid(g);
                    int n = r.Message == null ? Count(r) : -1;
                    if (n > bestN) { best = r; bestN = n; }
                }
                if (best.Message == null) best.Monday = WeekParser.MondayFromText(HeaderText(tables), allText.ToString());
                return best;
            }
            catch (Exception ex) { return new WeekParseResult { Message = "한글 파일을 읽다가 문제가 생겼어요. (" + ex.Message + ")" }; }
            finally { if (tmp != null) try { File.Delete(tmp); } catch { } }
        }

        // 한글 파일의 표들 (생일 명단 등)
        public static List<List<List<string>>> Tables(string path, out string msg)
        {
            msg = null; var tables = new List<List<List<string>>>();
            string tmp = null;
            try
            {
                if (Path.GetExtension(path).ToLowerInvariant() == ".hwp")
                {
                    tmp = HwpToHwpx(path);
                    if (tmp == null) { msg = "옛 한글(.hwp) 파일은 한글이 설치되어 있어야 읽을 수 있어요. HWPX로 저장해서 넣어 주세요."; return tables; }
                    path = tmp;
                }
                using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var z = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    var secs = new List<string>();
                    foreach (var e in z.Entries) if (Regex.IsMatch(e.FullName, @"^Contents/section\d+\.xml$", RegexOptions.IgnoreCase)) secs.Add(e.FullName);
                    secs.Sort();
                    foreach (var s in secs)
                    {
                        var d = new XmlDocument();
                        using (var st = z.GetEntry(s).Open()) d.Load(st);
                        foreach (XmlElement t in d.GetElementsByTagName("*")) if (t.LocalName == "tbl") tables.Add(Grid(t));
                    }
                }
            }
            catch (Exception ex) { msg = "한글 파일을 읽지 못했어요. (" + ex.Message + ")"; }
            finally { if (tmp != null) try { File.Delete(tmp); } catch { } }
            return tables;
        }

        static int Count(WeekParseResult r)
        {
            int n = 0;
            foreach (var kv in r.Days) foreach (var c in kv.Value) if (!string.IsNullOrEmpty(c.Subj) || !string.IsNullOrEmpty(c.Content)) n++;
            return n;
        }

        static string HeaderText(List<List<List<string>>> tables)
        {
            var sb = new StringBuilder();
            foreach (var g in tables) if (g.Count > 0) { foreach (var c in g[0]) sb.Append(c).Append(' '); foreach (var row in g) if (row.Count > 0) sb.Append(row[0]).Append(' '); }
            return sb.ToString();
        }

        static XmlElement Child(XmlElement e, string local)
        {
            foreach (XmlNode n in e.ChildNodes) if (n is XmlElement && n.LocalName == local) return (XmlElement)n;
            return null;
        }

        // 표 → 칸 글자 격자 (합친 칸은 덮는 칸마다 같은 글자)
        static List<List<string>> Grid(XmlElement tbl)
        {
            var g = new List<List<string>>();
            int autoRow = 0;
            foreach (XmlNode trn in tbl.ChildNodes)
            {
                var tr = trn as XmlElement; if (tr == null || tr.LocalName != "tr") continue;
                int autoCol = 0;
                foreach (XmlNode tcn in tr.ChildNodes)
                {
                    var tc = tcn as XmlElement; if (tc == null || tc.LocalName != "tc") continue;
                    int col = autoCol, row = autoRow, cs = 1, rs = 1;
                    var addr = Child(tc, "cellAddr");
                    if (addr != null) { int.TryParse(addr.GetAttribute("colAddr"), out col); int.TryParse(addr.GetAttribute("rowAddr"), out row); }
                    var span = Child(tc, "cellSpan");
                    if (span != null) { int.TryParse(span.GetAttribute("colSpan"), out cs); int.TryParse(span.GetAttribute("rowSpan"), out rs); }
                    cs = Math.Max(1, cs); rs = Math.Max(1, rs);
                    string text = CellText(tc);
                    for (int r = row; r < row + rs; r++)
                        for (int c = col; c < col + cs; c++)
                        {
                            while (g.Count <= r) g.Add(new List<string>());
                            while (g[r].Count <= c) g[r].Add(null);
                            g[r][c] = text;
                        }
                    autoCol = col + cs;
                }
                autoRow++;
            }
            return g;
        }

        // 칸 안의 문단들(중첩 표 제외)을 줄로
        static string CellText(XmlElement tc)
        {
            var lines = new List<string>();
            Walk(tc, lines, 0);
            var res = new List<string>();
            foreach (var l in lines) { var t = Regex.Replace(l, @"[ \t ]+", " ").Trim(); if (t.Length > 0) res.Add(t); }
            return string.Join("\n", res.ToArray());
        }
        static void Walk(XmlElement e, List<string> lines, int depth)
        {
            foreach (XmlNode n in e.ChildNodes)
            {
                var c = n as XmlElement; if (c == null) continue;
                if (c.LocalName == "tbl" && depth > 0) continue;
                if (c.LocalName == "p")
                {
                    var sb = new StringBuilder();
                    foreach (XmlElement t in c.GetElementsByTagName("*"))
                        if (t.LocalName == "t") sb.Append(t.InnerText);
                        else if (t.LocalName == "tab") sb.Append(' ');
                    lines.Add(sb.ToString());
                    continue;
                }
                Walk(c, lines, depth + 1);
            }
        }

        static readonly Regex DayRe = new Regex(@"(^|[\s(（\[<])(월|화|수|목|금)(요일)?([\s)）\]>]|$)");
        static readonly Regex PerRe = new Regex(@"^\(?\s*([1-7])\s*(교시|교)?\s*\)?(\s|$|\()");
        static int DayOf(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length > 24) return -1;
            var m = DayRe.Match(t.Replace("\n", " "));
            return m.Success ? "월화수목금".IndexOf(m.Groups[2].Value, StringComparison.Ordinal) : -1;
        }
        static int PerOf(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length > 20) return -1;
            var m = PerRe.Match(t.Trim());
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }

        // 요일 머리글이 가로줄이든 세로줄이든 찾아서 칸을 모은다
        public static WeekParseResult FromGrid(List<List<string>> g)
        {
            int rows = g.Count, cols = 0; foreach (var r in g) cols = Math.Max(cols, r.Count);
            Func<int, int, string> at = (r, c) => r < rows && c < g[r].Count ? g[r][c] : null;
            for (int pass = 0; pass < 2; pass++)
            {
                bool tr = pass == 1;   // tr: 요일이 세로줄(왼쪽), 교시가 가로줄(위)
                int A = tr ? cols : rows, B = tr ? rows : cols;
                Func<int, int, string> cell = (a, b) => tr ? at(b, a) : at(a, b);
                for (int a = 0; a < A; a++)
                {
                    var dayAt = new int[5]; for (int i = 0; i < 5; i++) dayAt[i] = -1;
                    int found = 0;
                    for (int b = 0; b < B; b++) { int d = DayOf(cell(a, b)); if (d >= 0 && dayAt[d] < 0 && (b == 0 || cell(a, b) != cell(a, b - 1))) { dayAt[d] = b; found++; } }
                    if (found < 4) continue;
                    int first = int.MaxValue; foreach (var v in dayAt) if (v >= 0) first = Math.Min(first, v);
                    // 교시 이름: 요일보다 앞쪽 줄들에서 찾기
                    var perAt = new int[8]; for (int i = 0; i < 8; i++) perAt[i] = -1;
                    for (int lb = 0; lb < first; lb++)
                        for (int aa = a + 1; aa < A; aa++)
                        {
                            int p = PerOf(cell(aa, lb));
                            if (p >= 1 && p <= 7 && perAt[p] < 0) perAt[p] = aa;
                        }
                    int periods = 0; while (periods < 7 && perAt[periods + 1] >= 0) periods++;
                    if (periods < 3) continue;
                    var res = new WeekParseResult { Periods = periods };
                    for (int d = 0; d < 5; d++)
                    {
                        var list = new List<WeekCell>();
                        for (int p = 1; p <= periods; p++)
                        {
                            string t = dayAt[d] >= 0 ? cell(perAt[p], dayAt[d]) : null;
                            list.Add(string.IsNullOrWhiteSpace(t) ? new WeekCell("", "") : WeekParser.Split(t));
                        }
                        res.Days[(d + 1).ToString()] = list;
                    }
                    return res;
                }
            }
            return new WeekParseResult { Message = "표에서 월·화·수·목·금과 1교시, 2교시 칸을 찾지 못했어요." };
        }

        // 컴퓨터에 깔린 한글로 .hwp → .hwpx 바꾸기
        static string HwpToHwpx(string path)
        {
            object hwp = null;
            try
            {
                var t = Type.GetTypeFromProgID("HWPFrame.HwpObject");
                if (t == null) return null;
                hwp = Activator.CreateInstance(t);
                try { t.InvokeMember("RegisterModule", System.Reflection.BindingFlags.InvokeMethod, null, hwp, new object[] { "FilePathCheckDLL", "FilePathCheckerModule" }); } catch { }
                string outp = Path.Combine(Path.GetTempPath(), "ramclock_week_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".hwpx");
                t.InvokeMember("Open", System.Reflection.BindingFlags.InvokeMethod, null, hwp, new object[] { path, "HWP", "forceopen:true" });
                t.InvokeMember("SaveAs", System.Reflection.BindingFlags.InvokeMethod, null, hwp, new object[] { outp, "HWPX", "" });
                return File.Exists(outp) ? outp : null;
            }
            catch { return null; }
            finally { try { if (hwp != null) hwp.GetType().InvokeMember("Quit", System.Reflection.BindingFlags.InvokeMethod, null, hwp, null); } catch { } }
        }
    }
}
