using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ClassroomHud
{
    public class RosterRow { public int No; public string Name = "", Gender = ""; public byte[] Photo; public string PhotoExt = ".png"; }

    // 엑셀(.xlsx/.xls)·CSV 명렬표에서 번호·이름·성별·사진을 읽는다
    public static class RosterImport
    {
        public static List<RosterRow> Read(string path, out string msg)
        {
            msg = null;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                List<List<string>> grid; Dictionary<int, KeyValuePair<byte[], string>> pics = new Dictionary<int, KeyValuePair<byte[], string>>();
                if (ext == ".csv" || ext == ".txt" || ext == ".tsv") grid = ReadCsv(path);
                else
                {
                    string x = path;
                    if (ext == ".xls" || ext == ".cell" || ext == ".ods")
                    {
                        x = ConvertWithExcel(path);
                        if (x == null) { msg = "이 파일 형식(" + ext + ")은 엑셀이나 한셀이 설치되어 있어야 읽을 수 있어요."; return null; }
                    }
                    grid = ReadXlsx(x, pics);
                }
                var rows = Interpret(grid, pics);
                if (rows.Count == 0) msg = "명단에서 학생 이름을 찾지 못했어요. 첫 줄에 '번호', '이름(성명)' 제목이 있으면 더 잘 읽어요.";
                return rows;
            }
            catch (Exception ex) { msg = "파일을 읽지 못했어요. 엑셀에서 열려 있다면 닫고 다시 해 주세요.\n(" + ex.Message + ")"; return null; }
        }

        // ---------- 표 해석 ----------
        static List<RosterRow> Interpret(List<List<string>> g, Dictionary<int, KeyValuePair<byte[], string>> pics)
        {
            int hdr = -1, cNo = -1, cName = -1, cGen = -1;
            for (int r = 0; r < Math.Min(15, g.Count) && hdr < 0; r++)
                for (int c = 0; c < g[r].Count; c++)
                {
                    string t = Regex.Replace(g[r][c] ?? "", @"\s", "");
                    if (t == "이름" || t == "성명" || t == "학생명" || t == "학생이름" || t == "성함") { hdr = r; cName = c; }
                }
            if (hdr >= 0)
            {
                for (int c = 0; c < g[hdr].Count; c++)
                {
                    string t = Regex.Replace(g[hdr][c] ?? "", @"\s", "");
                    if (cNo < 0 && (t == "번호" || t == "번" || t == "출석번호" || t == "No" || t == "NO" || t == "no")) cNo = c;
                    if (cGen < 0 && (t == "성별" || t == "남녀" || t == "남/여")) cGen = c;
                }
            }
            int start = hdr + 1;
            int cols = 0; foreach (var row in g) cols = Math.Max(cols, row.Count);
            // 제목 줄이 없거나 일부 열을 못 찾았으면 내용으로 짐작
            if (cName < 0 || cNo < 0 || cGen < 0)
            {
                for (int c = 0; c < cols; c++)
                {
                    int num = 0, kor = 0, gen = 0, tot = 0;
                    for (int r = start; r < g.Count; r++)
                    {
                        string v = Cell(g, r, c); if (v.Length == 0) continue; tot++;
                        if (Regex.IsMatch(v, @"^\d{1,2}(번)?$")) num++;
                        if (Regex.IsMatch(v, @"^[가-힣]{2,5}$") && Gender(v) == "") kor++;
                        if (Gender(v) != "") gen++;
                    }
                    if (tot == 0) continue;
                    if (cNo < 0 && num * 10 >= tot * 8) cNo = c;
                    else if (cName < 0 && kor * 10 >= tot * 6) cName = c;
                    else if (cGen < 0 && gen * 10 >= tot * 8) cGen = c;
                }
            }
            var list = new List<RosterRow>();
            if (cName < 0) return list;
            int auto = 1;
            for (int r = start; r < g.Count; r++)
            {
                string name = Cell(g, r, cName).Trim();
                if (name.Length == 0 || name == "이름" || name == "성명") continue;
                int no = -1;
                if (cNo >= 0) { var m = Regex.Match(Cell(g, r, cNo), @"\d{1,2}"); if (m.Success) no = int.Parse(m.Value); }
                if (no <= 0) no = auto;
                auto = no + 1;
                var rr = new RosterRow { No = no, Name = name, Gender = cGen >= 0 ? Gender(Cell(g, r, cGen)) : "" };
                KeyValuePair<byte[], string> p;
                if (pics.TryGetValue(r, out p)) { rr.Photo = p.Key; rr.PhotoExt = p.Value; }
                list.Add(rr);
            }
            return list;
        }

        static string Cell(List<List<string>> g, int r, int c) { return r < g.Count && c < g[r].Count ? (g[r][c] ?? "").Trim() : ""; }

        public static string Gender(string t)
        {
            t = (t ?? "").Trim().Trim('(', ')', '[', ']');
            if (t == "남" || t == "남자" || t == "M" || t == "m" || t == "male" || t == "Male") return "남";
            if (t == "여" || t == "여자" || t == "F" || t == "f" || t == "female" || t == "Female") return "여";
            return "";
        }

        // ---------- CSV ----------
        static List<List<string>> ReadCsv(string path)
        {
            var bytes = File.ReadAllBytes(path);
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch { text = Encoding.GetEncoding(949).GetString(bytes); }
            text = text.TrimStart('﻿');
            char sep = text.Contains("\t") ? '\t' : ',';
            var g = new List<List<string>>();
            foreach (var line in text.Replace("\r", "").Split('\n'))
            {
                var row = new List<string>(); var sb = new StringBuilder(); bool q = false;
                for (int i = 0; i < line.Length; i++)
                {
                    char ch = line[i];
                    if (ch == '"') { if (q && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = !q; }
                    else if (ch == sep && !q) { row.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(ch);
                }
                row.Add(sb.ToString());
                g.Add(row);
            }
            return g;
        }

        // ---------- XLSX ----------
        const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string NsPkg = "http://schemas.openxmlformats.org/package/2006/relationships";

        static XmlDocument Xml(ZipArchive z, string name)
        {
            var e = z.GetEntry(name);
            if (e == null) return null;
            var d = new XmlDocument();
            using (var s = e.Open()) d.Load(s);
            return d;
        }

        static string Resolve(string baseFile, string target)
        {
            if (target.StartsWith("/")) return target.TrimStart('/');
            var parts = new List<string>(baseFile.Split('/'));
            parts.RemoveAt(parts.Count - 1);
            foreach (var p in target.Split('/'))
            {
                if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else if (p != ".") parts.Add(p);
            }
            return string.Join("/", parts.ToArray());
        }

        static Dictionary<string, string> Rels(ZipArchive z, string file)
        {
            var map = new Dictionary<string, string>();
            int i = file.LastIndexOf('/');
            string rp = (i >= 0 ? file.Substring(0, i + 1) : "") + "_rels/" + file.Substring(i + 1) + ".rels";
            var d = Xml(z, rp);
            if (d == null) return map;
            foreach (XmlElement e in d.GetElementsByTagName("Relationship", NsPkg))
                map[e.GetAttribute("Id")] = Resolve(file, e.GetAttribute("Target"));
            return map;
        }

        static byte[] Bytes(ZipArchive z, string name)
        {
            var e = z.GetEntry(name);
            if (e == null) return null;
            using (var s = e.Open()) using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
        }

        static int ColIndex(string r)
        {
            int c = 0;
            foreach (char ch in r) { if (ch >= 'A' && ch <= 'Z') c = c * 26 + (ch - 'A' + 1); else break; }
            return c - 1;
        }

        static List<List<string>> ReadXlsx(string path, Dictionary<int, KeyValuePair<byte[], string>> pics)
        {
            using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                // 첫 번째 시트
                var wb = Xml(z, "xl/workbook.xml");
                var wbRels = Rels(z, "xl/workbook.xml");
                string sheet = "xl/worksheets/sheet1.xml";
                if (wb != null)
                {
                    var sh = wb.GetElementsByTagName("sheet", NsMain);
                    if (sh.Count > 0) { string rid = ((XmlElement)sh[0]).GetAttribute("id", NsRel); if (wbRels.ContainsKey(rid)) sheet = wbRels[rid]; }
                }
                // 공유 문자열
                var ss = new List<string>();
                var sd = Xml(z, "xl/sharedStrings.xml");
                if (sd != null)
                    foreach (XmlElement si in sd.GetElementsByTagName("si", NsMain))
                    {
                        var sb = new StringBuilder();
                        foreach (XmlElement t in si.GetElementsByTagName("t", NsMain))
                            if (t.ParentNode.LocalName != "rPh") sb.Append(t.InnerText);
                        ss.Add(sb.ToString());
                    }
                Dictionary<int, KeyValuePair<byte[], string>> cellPics;
                try { cellPics = CellImages(z); } catch { cellPics = new Dictionary<int, KeyValuePair<byte[], string>>(); }

                var g = new List<List<string>>();
                var doc = Xml(z, sheet);
                if (doc == null) return g;
                foreach (XmlElement row in doc.GetElementsByTagName("row", NsMain))
                {
                    int r; if (!int.TryParse(row.GetAttribute("r"), out r)) r = g.Count + 1;
                    r--;
                    while (g.Count <= r) g.Add(new List<string>());
                    foreach (XmlElement c in row.GetElementsByTagName("c", NsMain))
                    {
                        int col = ColIndex(c.GetAttribute("r"));
                        if (col < 0) col = g[r].Count;
                        string t = c.GetAttribute("t"), v = "";
                        if (t == "inlineStr") v = c.InnerText;
                        else
                        {
                            var vn = c.GetElementsByTagName("v", NsMain);
                            string raw = vn.Count > 0 ? vn[0].InnerText : "";
                            int si;
                            if (t == "s" && int.TryParse(raw, out si) && si < ss.Count) v = ss[si];
                            else v = raw;
                        }
                        // 셀 안에 넣은 그림 (엑셀 '셀에 배치')
                        string vm = c.GetAttribute("vm");
                        int vmi;
                        if (vm.Length > 0 && int.TryParse(vm, out vmi) && cellPics.ContainsKey(vmi) && !pics.ContainsKey(r)) pics[r] = cellPics[vmi];
                        while (g[r].Count <= col) g[r].Add("");
                        g[r][col] = v;
                    }
                }
                // 셀 위에 떠 있는 그림 → 그림이 걸친 행
                var sRels = Rels(z, sheet);
                foreach (XmlElement dr in doc.GetElementsByTagName("drawing", NsMain))
                {
                    string rid = dr.GetAttribute("id", NsRel);
                    if (!sRels.ContainsKey(rid)) continue;
                    string dfile = sRels[rid];
                    var dd = Xml(z, dfile); if (dd == null) continue;
                    var dRels = Rels(z, dfile);
                    foreach (XmlNode anchor in dd.DocumentElement.ChildNodes)
                    {
                        var ae = anchor as XmlElement; if (ae == null) continue;
                        int fromRow = -1, toRow = -1;
                        foreach (XmlNode ch in ae.ChildNodes)
                        {
                            var ce = ch as XmlElement; if (ce == null) continue;
                            if (ce.LocalName == "from" || ce.LocalName == "to")
                                foreach (XmlNode k in ce.ChildNodes)
                                    if (k.LocalName == "row") { int rv; if (int.TryParse(k.InnerText, out rv)) { if (ce.LocalName == "from") fromRow = rv; else toRow = rv; } }
                        }
                        if (fromRow < 0) continue;
                        string embed = null;
                        foreach (XmlElement b in ae.GetElementsByTagName("*"))
                            if (b.LocalName == "blip") { embed = b.GetAttribute("embed", NsRel); break; }
                        if (embed == null || !dRels.ContainsKey(embed)) continue;
                        var data = Bytes(z, dRels[embed]); if (data == null) continue;
                        int row = toRow > fromRow ? (fromRow + toRow) / 2 : fromRow;
                        if (toRow > fromRow && toRow - fromRow == 1) row = fromRow;
                        if (!pics.ContainsKey(row)) pics[row] = new KeyValuePair<byte[], string>(data, Path.GetExtension(dRels[embed]));
                    }
                }
                return g;
            }
        }

        // vm 번호 → 그림 (richData)
        static Dictionary<int, KeyValuePair<byte[], string>> CellImages(ZipArchive z)
        {
            var res = new Dictionary<int, KeyValuePair<byte[], string>>();
            var rv = Xml(z, "xl/richData/rdrichvalue.xml");
            var rel = Xml(z, "xl/richData/richValueRel.xml");
            if (rv == null || rel == null) return res;
            var relRels = Rels(z, "xl/richData/richValueRel.xml");
            var relIds = new List<string>();
            foreach (XmlElement e in rel.GetElementsByTagName("*"))
                if (e.LocalName == "rel") relIds.Add(e.GetAttribute("id", NsRel));
            var values = new List<int>();
            foreach (XmlElement e in rv.GetElementsByTagName("*"))
                if (e.LocalName == "rv")
                {
                    int idx = -1;
                    foreach (XmlNode v in e.ChildNodes) if (v.LocalName == "v") { int.TryParse(v.InnerText, out idx); break; }
                    values.Add(idx);
                }
            // metadata.xml: vm(1부터) → rich value 번호
            var vmToRv = new List<int>();
            var md = Xml(z, "xl/metadata.xml");
            if (md != null)
            {
                var fut = new List<int>();
                foreach (XmlElement e in md.GetElementsByTagName("*"))
                    if (e.LocalName == "rvb") { int i; int.TryParse(e.GetAttribute("i"), out i); fut.Add(i); }
                foreach (XmlElement e in md.GetElementsByTagName("*"))
                    if (e.LocalName == "valueMetadata")
                        foreach (XmlNode bkn in e.ChildNodes)
                        {
                            var bk = bkn as XmlElement; if (bk == null) continue;
                            int v = -1;
                            foreach (XmlElement rc in bk.GetElementsByTagName("*")) { int vv; if (rc.LocalName == "rc" && int.TryParse(rc.GetAttribute("v"), out vv)) v = vv; }
                            vmToRv.Add(v >= 0 && v < fut.Count ? fut[v] : vmToRv.Count);
                        }
            }
            int n = Math.Max(values.Count, vmToRv.Count);
            for (int vm = 1; vm <= n; vm++)
            {
                int rvi = vm - 1 < vmToRv.Count ? vmToRv[vm - 1] : vm - 1;
                if (rvi < 0 || rvi >= values.Count) continue;
                int ri = values[rvi];
                if (ri < 0 || ri >= relIds.Count || !relRels.ContainsKey(relIds[ri])) continue;
                var data = Bytes(z, relRels[relIds[ri]]);
                if (data != null) res[vm] = new KeyValuePair<byte[], string>(data, Path.GetExtension(relRels[relIds[ri]]));
            }
            return res;
        }

        // .xls 등: 설치된 엑셀/한셀로 .xlsx로 바꿔서 읽기
        static string ConvertWithExcel(string path)
        {
            foreach (var prog in new[] { "Excel.Application", "HCell.Application" })
            {
                object app = null;
                try
                {
                    var t = Type.GetTypeFromProgID(prog);
                    if (t == null) continue;
                    app = Activator.CreateInstance(t);
                    try { t.InvokeMember("DisplayAlerts", System.Reflection.BindingFlags.SetProperty, null, app, new object[] { false }); } catch { }
                    var books = t.InvokeMember("Workbooks", System.Reflection.BindingFlags.GetProperty, null, app, null);
                    var wb = books.GetType().InvokeMember("Open", System.Reflection.BindingFlags.InvokeMethod, null, books, new object[] { path });
                    string outp = Path.Combine(Path.GetTempPath(), "ramclock_roster_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".xlsx");
                    wb.GetType().InvokeMember("SaveAs", System.Reflection.BindingFlags.InvokeMethod, null, wb, new object[] { outp, 51 });
                    wb.GetType().InvokeMember("Close", System.Reflection.BindingFlags.InvokeMethod, null, wb, new object[] { false });
                    if (File.Exists(outp)) return outp;
                }
                catch { }
                finally
                {
                    try { if (app != null) app.GetType().InvokeMember("Quit", System.Reflection.BindingFlags.InvokeMethod, null, app, null); } catch { }
                }
            }
            return null;
        }

        // 명렬표 서식(.xlsx) 만들기: 번호·이름·성별·사진 칸
        public static void WriteTemplate(string path, List<string[]> rows)
        {
            Func<string, string> X = t => System.Security.SecurityElement.Escape(t ?? "");
            Func<int, int, string, int, string> C = (r, c, v, st) => "<c r=\"" + (char)('A' + c) + r + "\" t=\"inlineStr\" s=\"" + st + "\"><is><t>" + X(v) + "</t></is></c>";
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"" + NsMain + "\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sb.Append("<cols><col min=\"1\" max=\"1\" width=\"8\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"16\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"10\" customWidth=\"1\"/><col min=\"4\" max=\"4\" width=\"16\" customWidth=\"1\"/><col min=\"6\" max=\"6\" width=\"60\" customWidth=\"1\"/></cols><sheetData>");
            sb.Append("<row r=\"1\" ht=\"24\" customHeight=\"1\">" + C(1, 0, "번호", 1) + C(1, 1, "이름", 1) + C(1, 2, "성별", 1) + C(1, 3, "사진", 1) + C(1, 5, "※ 성별은 남/여로 적어요. 사진은 엑셀 [삽입 → 그림 → 셀에 배치]로 '사진' 칸에 넣거나, 그 줄 위에 올려 두면 돼요.", 2) + "</row>");
            for (int i = 0; i < rows.Count; i++)
            {
                int r = i + 2;
                sb.Append("<row r=\"" + r + "\" ht=\"60\" customHeight=\"1\"><c r=\"A" + r + "\" s=\"3\"><v>" + X(rows[i][0]) + "</v></c>" + C(r, 1, rows[i][1], 3) + C(r, 2, rows[i][2], 3) + C(r, 3, "", 3) + "</row>");
            }
            sb.Append("</sheetData><dataValidations count=\"1\"><dataValidation type=\"list\" allowBlank=\"1\" sqref=\"C2:C" + (rows.Count + 1) + "\"><formula1>\"남,여\"</formula1></dataValidation></dataValidations></worksheet>");
            string styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"" + NsMain + "\"><fonts count=\"3\"><font><sz val=\"11\"/><name val=\"맑은 고딕\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"맑은 고딕\"/></font><font><sz val=\"10\"/><color rgb=\"FF7A7F95\"/><name val=\"맑은 고딕\"/></font></fonts>"
                + "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF6C63FF\"/></patternFill></fill></fills>"
                + "<borders count=\"2\"><border/><border><left style=\"thin\"><color rgb=\"FFD5D8E5\"/></left><right style=\"thin\"><color rgb=\"FFD5D8E5\"/></right><top style=\"thin\"><color rgb=\"FFD5D8E5\"/></top><bottom style=\"thin\"><color rgb=\"FFD5D8E5\"/></bottom></border></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" xfId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf><xf numFmtId=\"0\" xfId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" applyFont=\"1\"/><xf numFmtId=\"0\" xfId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
            var files = new Dictionary<string, string>
            {
                { "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>" },
                { "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"" + NsPkg + "\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>" },
                { "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\"><sheets><sheet name=\"명렬표\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>" },
                { "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"" + NsPkg + "\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>" },
                { "xl/worksheets/sheet1.xml", sb.ToString() },
                { "xl/styles.xml", styles },
            };
            if (File.Exists(path)) File.Delete(path);
            using (var fs = File.Create(path))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
                foreach (var kv in files)
                    using (var w = new StreamWriter(z.CreateEntry(kv.Key).Open(), new UTF8Encoding(false))) w.Write(kv.Value);
        }

        // 사진 여러 장: 파일 이름에 들어 있는 이름이나 번호로 학생 찾기
        public static int MatchPhoto(string file, IList<string> names)
        {
            string bn = Path.GetFileNameWithoutExtension(file);
            int best = -1, bestLen = 0;
            for (int i = 0; i < names.Count; i++)
                if (!string.IsNullOrEmpty(names[i]) && bn.Contains(names[i]) && names[i].Length > bestLen) { best = i; bestLen = names[i].Length; }
            if (best >= 0) return best + 1;
            var m = Regex.Match(bn, @"^\D{0,6}?(\d{1,2})(?!\d)");
            if (m.Success) return int.Parse(m.Groups[1].Value);
            return -1;
        }
    }
}
