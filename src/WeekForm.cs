using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 이번 주 시간표: 주간학습안내 PDF 불러오기, 보기, 칸 고치기
    public class WeekForm : GlassForm
    {
        readonly Settings S;
        readonly Action save;
        DateTime monday;
        readonly Label lTitle, lNote;
        readonly DataGridView grid;
        readonly Button bPdf, bClear;
        static readonly string[] DayKo = { "월", "화", "수", "목", "금" };

        public WeekForm(Settings s, Action saveCb, DateTime day)
        {
            S = s; save = saveCb;
            monday = day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Media.UiFont(10F);
            Text = "이번 주 시간표";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(940, 640);
            MinimumSize = new Size(700, 480);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            var bPrev = new GButton { Text = "◀ 지난주", AutoSize = true, MinimumSize = new Size(0, 32) };
            var bNow = new GButton { Text = "이번 주", AutoSize = true, MinimumSize = new Size(0, 32) };
            var bNext = new GButton { Text = "다음 주 ▶", AutoSize = true, MinimumSize = new Size(0, 32) };
            lTitle = new Label { AutoSize = true, Font = Media.UiFont(14F, true), Margin = new Padding(14, 6, 3, 3) };
            bPrev.Click += (o, e) => { monday = monday.AddDays(-7); Load2(); };
            bNext.Click += (o, e) => { monday = monday.AddDays(7); Load2(); };
            bNow.Click += (o, e) => { var d = DateTime.Today; monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); Load2(); };
            top.Controls.AddRange(new Control[] { bPrev, bNow, bNext, lTitle });
            t.Controls.Add(top);

            lNote = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 6), MaximumSize = new Size(900, 0) };
            t.Controls.Add(lNote);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, BackgroundColor = SystemColors.Window,
                SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.Padding = new Padding(4, 6, 4, 6);
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.Font = Media.UiFont(10.5F, true);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "교시", FillWeight = 8, SortMode = DataGridViewColumnSortMode.NotSortable });
            for (int d = 0; d < 5; d++) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = DayKo[d], FillWeight = 18.4f, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns[0].DefaultCellStyle.Font = Media.UiFont(10F, true);
            grid.RowTemplate.Height = 72;
            grid.CellPainting += PaintCell;
            grid.CellDoubleClick += (o, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 1) EditCell(e.ColumnIndex, e.RowIndex); };
            t.Controls.Add(grid);

            var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var bImport = new GButton { Text = "주간학습안내 불러오기 (PDF·한글)…", AutoSize = true, MinimumSize = new Size(0, 36), Font = Media.UiFont(10F, true), Tag = "primary" };
            bPdf = new GButton { Text = "원본 PDF 열기", AutoSize = true, MinimumSize = new Size(0, 36) };
            bClear = new GButton { Text = "이 주 내용 지우기", AutoSize = true, MinimumSize = new Size(0, 36) };
            var bClose = new GButton { Text = "닫기", AutoSize = true, MinimumSize = new Size(90, 36), DialogResult = DialogResult.OK };
            closeBtn = bClose;
            bImport.Click += (o, e) => ImportPdf();
            bPdf.Click += (o, e) => { var wp = Plan(false); var p = wp != null ? Settings.MediaPath(wp.Pdf) : null; if (p != null) try { Process.Start(p); } catch { } };
            bClear.Click += (o, e) =>
            {
                if (MessageBox.Show(this, "이 주에 넣은 과목과 내용을 지울까요? 기본 시간표로 돌아가요.", "RamClock", MessageBoxButtons.YesNo) == DialogResult.Yes)
                { S.WeekPlans.Remove(Key); Save(); Load2(); }
            };
            bottom.Controls.AddRange(new Control[] { bImport, bPdf, bClear, bClose });
            t.Controls.Add(bottom);
            Controls.Add(t);
            Ui.Apply(this);
            CancelButton = bClose;
            Load2();
        }

        float DeviceDpi() { using (var g = CreateGraphics()) return g.DpiY; }

        string Key { get { return monday.ToString("yyyy-MM-dd"); } }

        WeekPlan Plan(bool create)
        {
            WeekPlan wp;
            if (S.WeekPlans.TryGetValue(Key, out wp) && wp != null) return wp;
            if (!create) return null;
            wp = new WeekPlan();
            S.WeekPlans[Key] = wp;
            return wp;
        }

        void Save() { if (save != null) save(); }

        int Rows()
        {
            int n = 0;
            for (int d = 1; d <= 5; d++)
            {
                int c = 0; foreach (var sl in Sched.Slots(S, d)) if (sl.Type == "class") c++;
                n = Math.Max(n, c);
            }
            var wp = Plan(false);
            if (wp != null) foreach (var kv in wp.Days) if (kv.Value != null) n = Math.Max(n, kv.Value.Count);
            return Math.Max(n, 1);
        }

        void Load2()
        {
            var fri = monday.AddDays(4);
            lTitle.Text = monday.Month + "월 " + monday.Day + "일(월) ~ " + fri.Month + "월 " + fri.Day + "일(금)";
            var wp = Plan(false);
            bPdf.Enabled = wp != null && Settings.MediaPath(wp.Pdf) != null;
            bClear.Enabled = wp != null;
            lNote.Text = wp == null
                ? "이 주는 아직 주간학습안내를 넣지 않았어요. 회색 글씨는 기본 시간표예요. [주간학습안내 불러오기]를 누르거나, PDF·한글 파일을 이 창에 끌어다 놓거나, 칸을 두 번 눌러 직접 적을 수 있어요."
                : "칸을 두 번 누르면 과목과 내용을 고칠 수 있어요. 여기 적힌 과목으로 위젯이 이 주에 교시를 안내해요.";
            grid.Rows.Clear();
            int rows = Rows();
            for (int i = 0; i < rows; i++) { int ri = grid.Rows.Add((i + 1) + "교시", "", "", "", "", ""); grid.Rows[ri].Height = (int)(72 * DeviceDpi() / 96f); }
            for (int d = 0; d < 5; d++)
                for (int i = 0; i < rows; i++)
                {
                    var date = monday.AddDays(d);
                    var wc = S.WeekCellOf(date, i + 1);
                    var cell = grid.Rows[i].Cells[d + 1];
                    if (wc != null) cell.Tag = wc;
                    else
                    {
                        var sb = S.SubjectOf(d + 1, i + 1);
                        cell.Tag = string.IsNullOrWhiteSpace(sb.Name) ? null : new WeekCell(sb.Name, "") { Move = sb.Move };
                        cell.Style.ForeColor = Color.Gray;
                    }
                    var w = cell.Tag as WeekCell;
                    cell.Value = w == null ? "" : (w.Subj + (w.Move ? " (이동)" : "") + (string.IsNullOrEmpty(w.Content) ? "" : "\n" + w.Content));
                    if (wc == null) cell.Tag = "base:" + (w != null ? w.Subj : "");
                }
            var today = DateTime.Today;
            for (int d = 0; d < 5; d++)
                grid.Columns[d + 1].HeaderText = DayKo[d] + " " + monday.AddDays(d).Day + "일" + (monday.AddDays(d) == today ? " · 오늘" : "");
        }

        void PaintCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            e.PaintBackground(e.CellBounds, true);
            if (e.ColumnIndex == 0)
            {
                using (var f0 = Media.UiFont(10.5F, true))
                    TextRenderer.DrawText(e.Graphics, (e.RowIndex + 1) + "교시", f0, e.CellBounds, Color.FromArgb(20, 33, 61), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                e.Handled = true; return;
            }
            var tag = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag;
            var r = new Rectangle(e.CellBounds.X + 6, e.CellBounds.Y + 6, e.CellBounds.Width - 12, e.CellBounds.Height - 10);
            using (var fb = Media.UiFont(10.5F, true)) using (var fr = Media.UiFont(9F))
            {
                if (tag is WeekCell)
                {
                    var w = (WeekCell)tag;
                    TextRenderer.DrawText(e.Graphics, w.Subj + (w.Move ? " (이동)" : ""), fb, r, Color.FromArgb(20, 33, 61), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
                    var sz = TextRenderer.MeasureText(w.Subj + " ", fb);
                    var r2 = new Rectangle(r.X, r.Y + sz.Height + 2, r.Width, Math.Max(0, r.Height - sz.Height - 2));
                    TextRenderer.DrawText(e.Graphics, w.Content ?? "", fr, r2, Color.FromArgb(80, 90, 110), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
                }
                else if (tag is string && ((string)tag).Length > 5)
                    TextRenderer.DrawText(e.Graphics, ((string)tag).Substring(5), fb, r, Color.Silver, TextFormatFlags.Left | TextFormatFlags.Top);
            }
            e.Handled = true;
        }

        void EditCell(int col, int row)
        {
            var wp = Plan(false);
            List<WeekCell> l = null;
            WeekCell cur = null;
            if (wp != null && wp.Days.TryGetValue(col.ToString(), out l) && l != null && row < l.Count) cur = l[row];
            if (cur == null)
            {
                var sb = S.SubjectOf(col, row + 1);
                cur = new WeekCell(sb.Name, "") { Move = sb.Move };
            }
            using (var dlg = new CellDialog(DayKo[col - 1] + "요일 " + (row + 1) + "교시", cur))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                wp = Plan(true);
                if (!wp.Days.TryGetValue(col.ToString(), out l) || l == null) { l = new List<WeekCell>(); wp.Days[col.ToString()] = l; }
                while (l.Count <= row) l.Add(new WeekCell("", ""));
                l[row] = dlg.Result;
                Save(); Load2();
            }
        }

        // 파일 끌어다 놓기: 창 어디에 놓아도 된다
        Button closeBtn;
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!TopLevel && closeBtn != null) closeBtn.Visible = false;   // 설정창 안에 들어갔을 때는 닫기 없음
            EnableDrop(this);
        }
        void EnableDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (o, e) =>
            {
                var f = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
                e.Effect = f != null && f.Length > 0 && WeekFile(f[0]) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            c.DragDrop += (o, e) =>
            {
                var f = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (f != null && f.Length > 0 && WeekFile(f[0])) BeginInvoke(new Action(() => ImportFile(f[0])));
            };
            foreach (Control ch in c.Controls) EnableDrop(ch);
        }

        void ImportPdf()
        {
            string path;
            using (var od = new OpenFileDialog { Filter = "주간학습안내 (PDF·한글)|*.pdf;*.hwpx;*.hwp|PDF 파일|*.pdf|한글 파일|*.hwpx;*.hwp", Title = "주간학습안내 파일 고르기" })
            {
                if (od.ShowDialog(this) != DialogResult.OK) return;
                path = od.FileName;
            }
            ImportFile(path);
        }

        static bool WeekFile(string p) { var e = Path.GetExtension(p ?? "").ToLowerInvariant(); return e == ".pdf" || e == ".hwpx" || e == ".hwp"; }

        void ImportFile(string path)
        {
            WeekParseResult res;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".hwpx" || ext == ".hwp")
            {
                Cursor = Cursors.WaitCursor;
                try { res = HwpxWeek.Parse(path); } finally { Cursor = Cursors.Default; }
                FinishImport(path, res);
                return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                var pdf = new PdfText(File.ReadAllBytes(path));
                if (pdf.Error != null) res = new WeekParseResult { Message = pdf.Error };
                else
                {
                    var runs = pdf.Extract();
                    res = WeekParser.Parse(runs, pdf.HLines, pdf.VLines);
                    if (res.Message != null || res.Days.Count == 0) WeekParser.Dump(runs, res.Message);
                }
            }
            catch (Exception ex) { res = new WeekParseResult { Message = "PDF를 읽다가 문제가 생겼어요. (" + ex.Message + ")" }; }
            finally { Cursor = Cursors.Default; }
            FinishImport(path, res);
        }

        void FinishImport(string path, WeekParseResult res)
        {

            DateTime target = monday;
            if (res.Monday.HasValue && res.Monday.Value != monday)
            {
                var m = res.Monday.Value; var f = m.AddDays(4);
                var ans = MessageBox.Show(this, "PDF에 적힌 날짜는 " + m.Month + "월 " + m.Day + "일(월) ~ " + f.Month + "월 " + f.Day + "일(금) 주예요.\n그 주에 넣을까요?\n\n[아니요]를 누르면 지금 보고 있는 주에 넣어요.", "RamClock", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel) return;
                if (ans == DialogResult.Yes) target = m;
            }
            monday = target;
            var wp = Plan(true);
            var name = Media.Import(path);
            if (name != null) wp.Pdf = name;
            if (res.Message == null && res.Days.Count > 0)
            {
                wp.Days = res.Days;
                Save(); Load2();
                MessageBox.Show(this, "주간학습안내에서 " + res.Periods + "교시까지 읽었어요.\n표를 보고 틀린 칸은 두 번 눌러 고쳐 주세요.", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Save(); Load2();
                MessageBox.Show(this, (res.Message ?? "표를 읽지 못했어요.") + "\n\nPDF는 이 주에 붙여 두었어요. [원본 PDF 열기]로 보면서 칸을 두 번 눌러 직접 적어 주세요.", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    public class CellDialog : GlassForm
    {
        public WeekCell Result;
        public CellDialog(string title, WeekCell cur)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Media.UiFont(10F);
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 320);
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = "과목", AutoSize = true });
            var tSubj = new TextBox { Text = cur.Subj ?? "", Dock = DockStyle.Fill };
            t.Controls.Add(tSubj);
            t.Controls.Add(new Label { Text = "학습 내용", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            var tCont = new TextBox { Text = cur.Content ?? "", Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
            t.Controls.Add(tCont);
            var cMove = new Toggle { Text = "이동 수업 (5분 전·3분 전 안내)", Checked = cur.Move, AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            t.Controls.Add(cMove);
            var bottom = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var bOk = new GButton { Text = "확인", AutoSize = true, MinimumSize = new Size(90, 32), DialogResult = DialogResult.OK };
            var bCancel = new GButton { Text = "취소", AutoSize = true, MinimumSize = new Size(90, 32), DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(bOk); bottom.Controls.Add(bCancel);
            t.Controls.Add(bottom);
            Controls.Add(t);
            Ui.Apply(this);
            AcceptButton = bOk; CancelButton = bCancel;
            FormClosing += (o, e) => { if (DialogResult == DialogResult.OK) Result = new WeekCell(tSubj.Text.Trim(), tCont.Text.Trim()) { Move = cMove.Checked }; };
        }
    }
}
