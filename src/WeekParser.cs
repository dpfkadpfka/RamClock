using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ClassroomHud
{
    // 주간학습안내 표: 요일 머리글(월~금)과 교시 칸(1~7)을 찾아 칸마다 글자를 모은다
    public class WeekParseResult
    {
        public Dictionary<string, List<WeekCell>> Days = new Dictionary<string, List<WeekCell>>();
        public DateTime? Monday;
        public int Periods;
        public string Message;
    }

    public static class WeekParser
    {
        static readonly string[] DayNames = { "월", "화", "수", "목", "금" };
        public static readonly string[] Subjects = {
            "국어","수학","사회","과학","영어","도덕","음악","미술","체육","실과","정보","창체","창의적 체험활동","자율","동아리","봉사","진로",
            "바른 생활","슬기로운 생활","즐거운 생활","바른생활","슬기로운생활","즐거운생활","통합","안전한 생활","안전한생활","학교자율","자율활동","동아리활동","진로활동","한문","보건","독서","놀이"
        };

        class Hit { public float X, Y, Size; public int Idx; }

        public static WeekParseResult Parse(List<PdfRun> runs) { return Parse(runs, null, null); }

        public static WeekParseResult Parse(List<PdfRun> runs, List<PdfLine> hl, List<PdfLine> vl)
        {
            WeekParseResult a = null, b = null;
            try { a = ParseCore(runs, hl, vl, null, false); } catch (Exception ex) { a = new WeekParseResult { Message = "표를 읽다가 문제가 생겼어요. (" + ex.Message + ")" }; }
            if (Environment.GetEnvironmentVariable("WP_DEBUG") == "1") Console.WriteLine("normal: " + a.Message + " filled " + Filled(a));
            if (a.Message == null && Filled(a) >= 8) return a;
            // 요일이 세로(왼쪽 줄), 교시가 가로(위쪽 줄)인 표: 가로세로를 바꿔서 다시 읽기
            if (runs != null && runs.Count > 0)
            {
                try
                {
                    var map = new Dictionary<PdfRun, PdfRun>();
                    var tr = new List<PdfRun>();
                    foreach (var x in runs)
                    {
                        float cx = x.X + x.W / 2, cy = x.Y + x.Size * 0.35f;
                        var n = new PdfRun { X = -cy, W = 0, Y = -cx - x.Size * 0.35f, Size = x.Size, Text = x.Text, Page = x.Page };
                        if (x.Parts != null)
                        {
                            n.Parts = new List<PdfRun>();
                            foreach (var g in x.Parts)
                            {
                                float gx = g.X + g.W / 2, gy = g.Y + g.Size * 0.35f;
                                var ng = new PdfRun { X = -gy, W = 0, Y = -gx - g.Size * 0.35f, Size = g.Size, Text = g.Text, Page = g.Page };
                                n.Parts.Add(ng); map[ng] = g;
                            }
                        }
                        tr.Add(n); map[n] = x;
                    }
                    List<PdfLine> th = null, tv = null;
                    if (hl != null && vl != null)
                    {
                        th = new List<PdfLine>(); tv = new List<PdfLine>();
                        foreach (var l in vl) th.Add(new PdfLine { A1 = -l.A2, A2 = -l.A1, P = -l.P, Page = l.Page });
                        foreach (var l in hl) tv.Add(new PdfLine { A1 = -l.A2, A2 = -l.A1, P = -l.P, Page = l.Page });
                    }
                    b = ParseCore(tr, th, tv, map, true);
                }
                catch { b = null; }
            }
            if (b != null && b.Message == null && (a.Message != null || Filled(b) > Filled(a))) { b.Monday = a.Monday ?? b.Monday; return b; }
            return a;
        }

        static int Filled(WeekParseResult r)
        {
            int n = 0;
            foreach (var kv in r.Days) foreach (var c in kv.Value) if (!string.IsNullOrEmpty(c.Subj) || !string.IsNullOrEmpty(c.Content)) n++;
            return n;
        }

        static WeekParseResult ParseCore(List<PdfRun> runs, List<PdfLine> hl, List<PdfLine> vl, Dictionary<PdfRun, PdfRun> orig, bool transposed)
        {
            var r = new WeekParseResult();
            if (runs == null || runs.Count == 0) { r.Message = "PDF에서 글자를 찾지 못했어요. 스캔한 그림 PDF는 읽을 수 없어요."; return r; }
            r.Monday = FindMonday(runs);

            int bestPage = -1; List<Hit> bestDays = null;
            foreach (int page in Pages(runs))
            {
                var d = DayHeaders(runs, page);
                if (d != null && (bestDays == null || d.Count > bestDays.Count)) { bestDays = d; bestPage = page; }
            }
            if (bestDays == null || bestDays.Count < 4) { r.Message = "표에서 월·화·수·목·금 머리글을 찾지 못했어요."; return r; }

            float headerY = 0; foreach (var h in bestDays) headerY += h.Y; headerY /= bestDays.Count;
            float firstX = float.MaxValue; foreach (var h in bestDays) firstX = Math.Min(firstX, h.X);
            float colGap = (bestDays[bestDays.Count - 1].X - bestDays[0].X) / Math.Max(1, bestDays.Count - 1);

            // 교시 칸 찾기: 월요일 열보다 왼쪽, 머리글보다 아래
            var labels = new SortedDictionary<int, Hit>();
            var re = new Regex(@"^\(?\s*([1-7])\s*(교시|교)?\s*\)?(\s|$|\()");
            foreach (var run in runs)
            {
                if (run.Page != bestPage || run.Y >= headerY - run.Size * 0.5f) continue;
                float cx = run.X + run.W / 2;
                if (cx > firstX - colGap * 0.45f) continue;
                var m = re.Match(run.Text.Trim());
                if (!m.Success) continue;
                int n = int.Parse(m.Groups[1].Value);
                if (!labels.ContainsKey(n)) labels[n] = new Hit { X = cx, Y = run.Y + run.Size * 0.35f, Size = run.Size, Idx = n };
            }
            var lab = new List<Hit>();
            foreach (var kv in labels) if (lab.Count == 0 || kv.Value.Y < lab[lab.Count - 1].Y) lab.Add(kv.Value);
            // 1부터 연속된 것만
            var seq = new List<Hit>();
            for (int i = 0; i < lab.Count; i++) { if (lab[i].Idx != seq.Count + 1) break; seq.Add(lab[i]); }
            if (seq.Count < 3) { r.Message = "표에서 1교시, 2교시 같은 교시 칸을 찾지 못했어요."; return r; }
            r.Periods = seq.Count;

            // 경계
            var colL = new float[bestDays.Count]; var colR = new float[bestDays.Count];
            for (int i = 0; i < bestDays.Count; i++)
            {
                float left = i > 0 ? (bestDays[i - 1].X + bestDays[i].X) / 2 : bestDays[i].X - colGap / 2;
                float right = i < bestDays.Count - 1 ? (bestDays[i].X + bestDays[i + 1].X) / 2 : bestDays[i].X + colGap / 2;
                colL[i] = left; colR[i] = right;
            }
            var rowT = new float[seq.Count]; var rowB = new float[seq.Count];
            for (int i = 0; i < seq.Count; i++)
            {
                float gapUp = i > 0 ? (seq[i - 1].Y - seq[i].Y) / 2 : (i + 1 < seq.Count ? (seq[i].Y - seq[i + 1].Y) / 2 : seq[i].Size * 2);
                float gapDn = i + 1 < seq.Count ? (seq[i].Y - seq[i + 1].Y) / 2 : gapUp;
                rowT[i] = Math.Min(seq[i].Y + gapUp, headerY - seq[i].Size * 0.6f);
                rowB[i] = seq[i].Y - gapDn;
            }

            // 표 테두리 선이 있으면 그 선으로 칸 경계를 잡는다
            if (hl != null && vl != null)
            {
                float midY = seq[0].Y;
                for (int i = 0; i < seq.Count; i++)
                {
                    float above = float.MaxValue, below = float.MinValue;
                    foreach (var ln in hl)
                    {
                        if (ln.Page != bestPage || ln.A1 > seq[i].X + 1 || ln.A2 < seq[i].X - 1) continue;
                        if (ln.P > seq[i].Y && ln.P < above) above = ln.P;
                        if (ln.P < seq[i].Y && ln.P > below) below = ln.P;
                    }
                    if (above != float.MaxValue && below != float.MinValue && above - below < seq[i].Size * 30) { rowT[i] = above; rowB[i] = below; }
                }
                for (int i = 0; i < bestDays.Count; i++)
                {
                    float left = float.MinValue, right = float.MaxValue;
                    foreach (var ln in vl)
                    {
                        if (ln.Page != bestPage || ln.A1 > midY + 1 || ln.A2 < midY - 1) continue;
                        if (ln.P < bestDays[i].X && ln.P > left) left = ln.P;
                        if (ln.P > bestDays[i].X && ln.P < right) right = ln.P;
                    }
                    if (left != float.MinValue && right != float.MaxValue && right - left < colGap * 1.8f) { colL[i] = left; colR[i] = right; }
                }
            }
            else
            {
                // 선이 없을 때: 교시 글자가 칸 위쪽에 붙어 있으면 위쪽 기준으로 나눈다
                int topAligned = 0, total = 0;
                foreach (var run in runs)
                {
                    if (run.Page != bestPage || run.X + run.W / 2 < colL[0]) continue;
                    if (Math.Abs(run.Y + run.Size * 0.35f - seq[0].Y) < seq[0].Size * 0.6f) topAligned++;
                    if (run.Y + run.Size * 0.35f <= rowT[0] && run.Y + run.Size * 0.35f > rowB[0]) total++;
                }
                if (topAligned >= 3)
                    for (int i = 0; i < seq.Count; i++)
                    {
                        rowT[i] = seq[i].Y + seq[i].Size * 0.7f;
                        rowB[i] = i + 1 < seq.Count ? seq[i + 1].Y + seq[i + 1].Size * 0.7f : seq[i].Y - (seq[i].Size * 2.5f + (i > 0 ? seq[i - 1].Y - seq[i].Y : 0));
                    }
            }

            var cells = new List<PdfRun>[5, 7];
            Func<PdfRun, int[]> where = run =>
            {
                float cx = run.X + run.W / 2, cy = run.Y + run.Size * 0.35f;
                int col = -1, row = -1;
                for (int i = 0; i < bestDays.Count; i++) if (cx >= colL[i] && cx < colR[i]) { col = bestDays[i].Idx; break; }
                for (int i = 0; i < seq.Count; i++) if (cy <= rowT[i] && cy > rowB[i]) { row = i; break; }
                return new[] { col, row };
            };
            foreach (var run in runs)
            {
                if (run.Page != bestPage) continue;
                var pieces = new List<PdfRun> { run };
                // 칸 사이가 좁아 옆 칸 글자와 한 덩어리로 붙은 경우 글자 단위로 다시 나눈다
                if (run.Parts != null && run.Parts.Count > 1)
                {
                    var a = where(run.Parts[0]); var z = where(run.Parts[run.Parts.Count - 1]);
                    if (a[0] != z[0] || a[1] != z[1])
                    {
                        pieces = run.Parts;
                        if (Environment.GetEnvironmentVariable("WP_DEBUG") == "1") Console.WriteLine("explode " + run.Text + " " + a[0] + "," + a[1] + " -> " + z[0] + "," + z[1] + " parts " + run.Parts.Count);
                    }
                }
                foreach (var pc in pieces)
                {
                    var w = where(pc);
                    if (w[0] < 0 || w[1] < 0) continue;
                    if (cells[w[0], w[1]] == null) cells[w[0], w[1]] = new List<PdfRun>();
                    cells[w[0], w[1]].Add(pc);
                }
            }

            for (int d = 0; d < 5; d++)
            {
                var list = new List<WeekCell>();
                for (int p = 0; p < seq.Count; p++)
                {
                    var c = cells[d, p];
                    if (c == null) { list.Add(new WeekCell("", "")); continue; }
                    if (orig != null) { var oc = new List<PdfRun>(); foreach (var x in c) oc.Add(orig[x]); c = oc; }
                    c.Sort((a, b) => Math.Abs(a.Y - b.Y) > Math.Min(a.Size, b.Size) * 0.4f ? b.Y.CompareTo(a.Y) : a.X.CompareTo(b.X));
                    var lines = new List<string>(); var rights = new List<float>(); var lefts = new List<float>(); float lastY = float.NaN, sz = 10;
                    foreach (var x in c)
                    {
                        if (lines.Count > 0 && Math.Abs(x.Y - lastY) <= x.Size * 0.4f)
                        {
                            bool sp = x.X - rights[rights.Count - 1] > x.Size * 0.2f || (x.Text.Length > 1 && x.X - rights[rights.Count - 1] > -x.Size * 0.1f);
                            if (x.Text.Length == 1 && x.X - rights[rights.Count - 1] <= x.Size * 0.2f) sp = false;
                            lines[lines.Count - 1] += (sp ? " " : "") + x.Text;
                            rights[rights.Count - 1] = Math.Max(rights[rights.Count - 1], x.X + x.W);
                        }
                        else { lines.Add(x.Text); rights.Add(x.X + x.W); lefts.Add(x.X); }
                        lastY = x.Y; sz = x.Size;
                    }
                    // 칸 끝에서 줄이 바뀐 것은 이어 붙이기
                    float cellR = 0; for (int i = 0; i < bestDays.Count; i++) if (bestDays[i].Idx == d) cellR = colR[i];
                    float cellL = 0; for (int i = 0; i < bestDays.Count; i++) if (bestDays[i].Idx == d) cellL = colL[i];
                    if (transposed) { cellR = -rowB[p]; cellL = -rowT[p]; }
                    var sb = new System.Text.StringBuilder(lines[0]);
                    for (int i = 1; i < lines.Count; i++)
                    {
                        string prev = lines[i - 1], cur = lines[i];
                        bool wrapped = i > 1 && rights[i - 1] >= cellR - sz * 4.5f;
                        bool centered = Math.Abs((lefts[i - 1] - cellL) - (cellR - rights[i - 1])) < sz * 1.2f && lefts[i - 1] - cellL > sz * 0.8f;
                        // 두 줄의 가운데가 같으면 가운데 정렬 글 → 낱말 단위 줄바꿈이니 띄어 쓴다
                        if (Math.Abs((lefts[i - 1] + rights[i - 1]) / 2 - (lefts[i] + rights[i]) / 2) < sz * 0.6f && Math.Abs(lefts[i - 1] - lefts[i]) > sz * 0.8f) centered = true;
                        bool charWrap = rights[i - 1] >= cellR - sz * 1.3f && !centered;
                        if (wrapped && charWrap && Hangul(prev[prev.Length - 1]) && Hangul(cur[0])) sb.Append(cur);
                        else if (wrapped) sb.Append(" ").Append(cur);
                        else sb.Append("\n").Append(cur);
                    }
                    if (Environment.GetEnvironmentVariable("WP_DEBUG") == "1") Console.WriteLine("CELL[" + sb.ToString().Replace("\n", "\\n") + "]");
                    list.Add(Split(sb.ToString()));
                }
                r.Days[(d + 1).ToString()] = list;
            }
            return r;
        }

        // 못 읽었을 때 원인을 알 수 있게 꺼낸 글자를 남겨 둔다
        public static void Dump(List<PdfRun> runs, string msg)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Settings.Dir);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + msg);
                sb.AppendLine("글자 조각 " + (runs == null ? 0 : runs.Count) + "개");
                if (runs != null) foreach (var x in runs) sb.AppendLine("p" + x.Page + "  x=" + x.X.ToString("0") + " y=" + x.Y.ToString("0") + " s=" + x.Size.ToString("0.0") + "  " + x.Text);
                System.IO.File.WriteAllText(System.IO.Path.Combine(Settings.Dir, "week_debug.txt"), sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { }
        }

        static bool Hangul(char ch) { return ch >= '\uAC00' && ch <= '\uD7A3'; }

        static List<int> Pages(List<PdfRun> runs)
        {
            var l = new List<int>();
            foreach (var x in runs) if (!l.Contains(x.Page)) l.Add(x.Page);
            return l;
        }

        // 월~금 머리글: 같은 높이에 왼쪽부터 순서대로 놓인 것
        static List<Hit> DayHeaders(List<PdfRun> runs, int page)
        {
            var re = new Regex(@"(^|[\s(（\[<])(월|화|수|목|금)(요일)?([\s)）\]>(（]|$)");
            var cands = new List<Hit>();
            foreach (var run in runs)
            {
                if (run.Page != page || run.Text.Length > 16) continue;
                var m = re.Match(run.Text);
                if (!m.Success) continue;
                cands.Add(new Hit { X = run.X + run.W / 2, Y = run.Y, Size = run.Size, Idx = Array.IndexOf(DayNames, m.Groups[2].Value) });
            }
            List<Hit> best = null;
            foreach (var c in cands)
            {
                var row = new Hit[5];
                foreach (var o in cands)
                    if (Math.Abs(o.Y - c.Y) <= Math.Max(c.Size, o.Size) * 1.6f && (row[o.Idx] == null || Math.Abs(o.Y - c.Y) < Math.Abs(row[o.Idx].Y - c.Y))) row[o.Idx] = o;
                var l = new List<Hit>();
                foreach (var h in row) if (h != null) l.Add(h);
                bool ordered = true;
                for (int i = 1; i < l.Count; i++) if (l[i].X <= l[i - 1].X) ordered = false;
                if (ordered && (best == null || l.Count > best.Count)) best = l;
            }
            return best;
        }

        // 첫 줄에서 과목 이름을 떼어 내고 나머지는 학습 내용으로
        public static WeekCell Split(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return new WeekCell("", "");
            // 첫 줄이 짧은 과목 이름이면(자간 때문에 "국 어(가)"처럼 띄어진 것 포함) 그대로 과목으로
            int nl = text.IndexOf('\n');
            if (nl > 0)
            {
                string line1 = Regex.Replace(text.Substring(0, nl), @"\s+", "");
                if (line1.Length >= 1 && line1.Length <= 9 && Regex.IsMatch(line1, @"^[\[\(<【〔]?[가-힣A-Za-z]{1,6}[\]\)>】〕]?(\([가-힣A-Za-z0-9]{1,3}\))?$"))
                    return new WeekCell(line1.Trim('[', ']', '<', '>', '【', '】', '〔', '〕'), Regex.Replace(text.Substring(nl + 1).Trim(), @"[ \t]+", " "));
            }
            string flat = Regex.Replace(text, @"\s+", " ");
            foreach (var s in Subjects)
            {
                var m = Regex.Match(flat, @"^[\[\(<【〔]?\s*" + Regex.Escape(s) + @"\s*[\]\)>】〕]?");
                if (m.Success)
                {
                    string rest = text.Substring(Math.Min(text.Length, IndexAfter(text, m.Length))).Trim().TrimStart(':', '-', '·', '|', ')', ']').Trim();
                    return new WeekCell(s.Replace(" ", ""), rest);
                }
            }
            var first = Regex.Match(flat, @"^[\[\(<【〔]?\s*([가-힣A-Za-z]{1,8})");
            if (first.Success && first.Groups[1].Value.Length <= 6)
            {
                string rest = flat.Substring(first.Length).Trim().TrimStart(']', ')', '>', '】', '〕', ':', '-').Trim();
                return new WeekCell(first.Groups[1].Value, rest);
            }
            return new WeekCell("", text);
        }

        // 공백을 하나로 줄인 문자열 기준 길이를 원래 문자열 위치로 바꾸기
        static int IndexAfter(string text, int flatLen)
        {
            int f = 0, i = 0; bool lastWs = false;
            while (i < text.Length && f < flatLen)
            {
                bool ws = char.IsWhiteSpace(text[i]);
                if (!(ws && lastWs)) f++;
                lastWs = ws; i++;
            }
            return i;
        }

        // 글자에서 날짜 찾기 (머리글 쪽 날짜를 더 믿는다)
        public static DateTime? MondayFromText(string header, string all)
        {
            var runs = new List<PdfRun> { new PdfRun { Page = 0, Y = 100, Text = header ?? "", Size = 10 }, new PdfRun { Page = 0, Y = 0, Text = all ?? "", Size = 10 } };
            return FindMonday(runs);
        }

        static DateTime? FindMonday(List<PdfRun> runs)
        {
            int year = DateTime.Now.Year;
            var all = new System.Text.StringBuilder();
            foreach (var r in runs) all.Append(r.Text).Append(' ');
            string s = all.ToString();
            var ym = Regex.Match(s, @"(20\d\d)\s*[.년]");
            if (ym.Success) year = int.Parse(ym.Groups[1].Value);
            // 첫 쪽 위쪽(제목·요일 머리글)에 있는 날짜를 훨씬 중요하게 본다 — 아래 알림글의 날짜에 끌려가지 않게
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var r in runs) if (r.Page == 0) { top = Math.Max(top, r.Y); bottom = Math.Min(bottom, r.Y); }
            var counts = new Dictionary<DateTime, int>();
            var re = new Regex(@"(?<!\d)(1[0-2]|0?[1-9])\s*(?:월|[./])\s*(3[01]|[12]\d|0?[1-9])\s*일?");
            foreach (var r in runs)
            {
                int w = r.Page == 0 && top > bottom && r.Y >= top - (top - bottom) * 0.3f ? 10 : 1;
                foreach (Match m in re.Matches(r.Text))
                {
                    DateTime d;
                    try { d = new DateTime(year, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)); } catch { continue; }
                    var mon = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
                    int c; counts.TryGetValue(mon, out c); counts[mon] = c + w;
                }
            }
            if (counts.Count == 0) return null;
            DateTime best = DateTime.MinValue; int bc = 0;
            foreach (var kv in counts) if (kv.Value > bc) { bc = kv.Value; best = kv.Key; }
            return best;
        }
    }
}
