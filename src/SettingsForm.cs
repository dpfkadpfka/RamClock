using System.IO;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ClassroomHud
{
    public class SettingsForm : GlassForm
    {
        public Settings R;
        NavBar nav; Panel content; readonly List<Panel> pages = new List<Panel>(); readonly List<string> titles = new List<string>(); int curPage = -1; bool navLock;
        PaintBox preview; Bitmap previewBmp; ComboBox cbBack; Timer previewTimer; FlowLayoutPanel themeFlow;
        ComboBox cbBgStyle, cbClockFont; Swatch bBg1, bBg2, bOutlineColor, bSubColor, bBorderColor, bClockColor, bClockOutlineColor; Button bBgImg;
        Slider tbOpacity, tbRadius, tbOutline, tbClockOutline; CheckBox chkGlow, chkBorder, chkBold, chkClockOutline, chkClockBold;
        RadioButton rbSeg, rbFontDigits; List<Swatch> clockSwatches;
        Label lFontFile, lClockFile;
        // 시정표
        ComboBox cbSchedDay; CheckBox chkOverride; DataGridView gSched; Button bAdd, bDel, bUp, bDown, bReset; Label lSchedNote;
        int schedDay;
        bool loading;
        // 시간표
        ComboBox cbTtDay; DataGridView gTt; int ttDay = 1; bool ttLoaded;
        // 음성·안내
        CheckBox chkVoice, chkSfx; ComboBox cbVoice; Slider slRate; DataGridView gRules, gMsgs;
        // 화면
        CheckBox chkMemoBg, chkMemoBold, chkMemoLine; Slider slMemoSize, slMemoAlpha; Swatch bMemoColor, bMemoBg, bMemoLine; ComboBox cbMemoFont;
        CheckBox chkSec, chkTop, chkDate, chkStatus, chkReadable, chkLocked, chkAuto, chkOutline, chkShadow, chkDigitFont;
        ComboBox cbFont; NumericUpDown nBanner; TextBox txtPresets; Panel colorPreview; Label lColor, lPosNote, lFontPreview;
        // 뽑기
        NumericUpDown nCount; RadioButton rbBoth, rbName, rbNo; CheckBox chkNoRepeat, chkResetStart; DataGridView gStu; Label lPicked;

        class MsgTag { public string Sound, Image; }

        static readonly string[] TypeKo = { "교시", "쉬는 시간", "점심시간" };
        static readonly string[] TypeEn = { "class", "break", "lunch" };
        static string Ko(string en) { int i = Array.IndexOf(TypeEn, en); return TypeKo[i < 0 ? 0 : i]; }
        static string En(object ko) { int i = Array.IndexOf(TypeKo, ko as string); return TypeEn[i < 0 ? 0 : i]; }
        static readonly string[] DayKo = { "", "월", "화", "수", "목", "금" };

        public Action<Settings> LivePreview;
        public Action<Settings> Saved;
        Label lSaved; Timer savedTimer;
        public int TabCount { get { return pages.Count; } }
        public void SelectTab(int i) { nav.SelectedIndex = i; }
        public void SelectTab(string title) { int i = titles.IndexOf(title); if (i >= 0) nav.SelectedIndex = i; }

        public SettingsForm(Settings s)
        {
            R = s;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Media.UiFont(10F);
            Text = "RamClock 설정";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1040, 700);
            MinimumSize = new Size(820, 560);
            BackColor = Ui.Bg;
            ShowInTaskbar = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Padding = new Padding(14, 14, 14, 0);
            nav = new NavBar { Dock = DockStyle.Fill, ItemHeight = 48 };
            var navWrap = new GlassPanel { Dock = DockStyle.Left, Width = 200, Padding = new Padding(8, 12, 8, 8), Alpha = 120 };
            var brand = new Label { Text = "RamClock", Dock = DockStyle.Top, Height = 48, Font = Media.UiFont(15F, true), ForeColor = Ui.Ink, Padding = new Padding(14, 0, 0, 8), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            navWrap.Controls.Add(nav); navWrap.Controls.Add(brand);
            var gap = new Panel { Dock = DockStyle.Left, Width = 14, BackColor = Color.Transparent };
            content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(4, 2, 0, 0) };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 10, 0, 12), BackColor = Color.Transparent };
            var bSave = new GButton { Text = "저장", AutoSize = true, MinimumSize = new Size(120, 40), Font = Media.UiFont(10.5F, true), Tag = "primary" };
            var bCancel = new GButton { Text = "닫기", AutoSize = true, MinimumSize = new Size(100, 40), DialogResult = DialogResult.Cancel };
            bSave.Click += (o, e) => SaveAndClose();
            bCancel.Click += (o, e) => Close();
            bottom.Controls.Add(bSave); bottom.Controls.Add(bCancel);
            lSaved = new Label { Text = "[저장]을 눌러야 위젯에 적용돼요. 저장해도 창은 닫히지 않아요.", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(3, 12, 12, 3), BackColor = Color.Transparent };
            bottom.Controls.Add(lSaved);
            savedTimer = new Timer { Interval = 2500 };
            savedTimer.Tick += (o, e) => { savedTimer.Stop(); lSaved.ForeColor = Ui.Muted; lSaved.Text = "[저장]을 눌러야 위젯에 적용돼요. 저장해도 창은 닫히지 않아요."; };
            CancelButton = bCancel;
            Controls.Add(content);
            Controls.Add(gap);
            Controls.Add(navWrap);
            Controls.Add(bottom);

            BuildScreen(); BuildSched(); BuildTt(); BuildRules(); BuildMsgs(); BuildPick(); BuildLinks(); BuildHelp();
            nav.SelectedIndexChanged += (o, e) => ShowPage(nav.SelectedIndex);
            Ui.Apply(this);
            nav.SelectedIndex = 0;
            Shown += (o, e) => RenderPreview();
            ResumeLayout(false);
            PerformLayout();
        }

        // ---------- 공용 ----------
        static DataGridView NewGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                BackgroundColor = SystemColors.Window,
                EditMode = DataGridViewEditMode.EditOnEnter,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false
            };
            g.DataError += (o, e) => { e.ThrowException = false; };
            g.CurrentCellDirtyStateChanged += (o, e) =>
            {
                if (g.IsCurrentCellDirty && (g.CurrentCell is DataGridViewCheckBoxCell || g.CurrentCell is DataGridViewComboBoxCell))
                    g.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            return g;
        }
        static DataGridViewTextBoxColumn TextCol(string h, float w, bool ro = false)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = h, FillWeight = w, ReadOnly = ro, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (ro) c.DefaultCellStyle.ForeColor = Color.DimGray;
            return c;
        }
        static DataGridViewCheckBoxColumn CheckCol(string h, float w) { return new DataGridViewCheckBoxColumn { HeaderText = h, FillWeight = w }; }

        // 줄마다 맨 오른쪽 ✕ 버튼: 누르면 그 줄 삭제
        static void AddDeleteCol(DataGridView g, Action after)
        {
            var c = new DataGridViewButtonColumn { Name = "del", HeaderText = "", Text = "✕", UseColumnTextForButtonValue = true, FillWeight = 5, MinimumWidth = 38 };
            g.Columns.Add(c);
            g.CellContentClick += (o, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != c.Index || g.ReadOnly) return;
                g.EndEdit();
                g.Rows.RemoveAt(e.RowIndex);
                if (after != null) after();
            };
        }

        static void MoveGrid(DataGridView g, int d)
        {
            g.EndEdit();
            int r = CurRow(g), t = r + d;
            if (r < 0 || t < 0 || t >= g.Rows.Count) return;
            int ci = g.CurrentCell != null ? g.CurrentCell.ColumnIndex : 0;
            var row = g.Rows[r]; g.Rows.RemoveAt(r); g.Rows.Insert(t, row);
            g.CurrentCell = g.Rows[t].Cells[ci];
        }

        bool AskReset(string what) { return MessageBox.Show(this, what + "\n지금 적어 둔 내용은 사라져요. 계속할까요?", "RamClock", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }

        static Button Arrow(string t, string tip) { var b = new GButton { Text = t, AutoSize = false, Size = new Size(40, 36), Margin = new Padding(0, 2, 6, 2) }; new ToolTip().SetToolTip(b, tip); return b; }
        static DataGridViewComboBoxColumn ComboCol(string h, float w, params string[] items)
        {
            var c = new DataGridViewComboBoxColumn { HeaderText = h, FillWeight = w, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton, FlatStyle = FlatStyle.Flat };
            c.Items.AddRange(items);
            return c;
        }
        static DataGridViewButtonColumn BtnCol(string text, float w) { return new DataGridViewButtonColumn { HeaderText = "", Text = text, UseColumnTextForButtonValue = true, FillWeight = w }; }
        static Button B(string t) { return new GButton { Text = t, AutoSize = true, MinimumSize = new Size(0, 36), Margin = new Padding(0, 2, 8, 2) }; }
        static Label Note(string t) { return new Label { Text = t, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 3), MaximumSize = new Size(860, 0) }; }
        static Label Head(string t) { return new Label { Text = t, AutoSize = true, Font = Media.UiFont(10.5F, true), Margin = new Padding(3, 12, 3, 4) }; }
        static FlowLayoutPanel Flow() { return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) }; }
        static string SoundText(string name) { return string.IsNullOrEmpty(name) ? "글자 읽기" : "♪ " + Media.Display(name); }
        static string ImageText(string name) { return string.IsNullOrEmpty(name) ? "없음" : "▣ " + Media.Display(name); }

        Panel Page(string title, Control[] rows, float[] percents) { return Page(title, rows, percents, true); }

        Panel Page(string title, Control[] rows, float[] percents, bool glass)
        {
            Panel pg = glass ? (Panel)new GlassPanel { Padding = new Padding(18, 14, 18, 14), Alpha = 135 } : new Panel { BackColor = Color.Transparent };
            pg.Dock = DockStyle.Fill; pg.Visible = false;
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length + 1, BackColor = Color.Transparent };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = title, AutoSize = true, Font = Media.UiFont(17F, true), ForeColor = Ui.Ink, Margin = new Padding(2, 0, 0, 12), BackColor = Color.Transparent }, 0, 0);
            for (int i = 0; i < rows.Length; i++)
            {
                t.RowStyles.Add(percents[i] > 0 ? new RowStyle(SizeType.Percent, percents[i]) : new RowStyle(SizeType.AutoSize));
                rows[i].Dock = DockStyle.Fill;
                t.Controls.Add(rows[i], 0, i + 1);
            }
            pg.Controls.Add(t);
            content.Controls.Add(pg);
            pages.Add(pg); titles.Add(title);
            nav.Items.Add(title);
            return pg;
        }

        void ShowPage(int i)
        {
            if (navLock || i < 0 || i == curPage) return;
            if (curPage >= 0)
            {
                string leaving = titles[curPage];
                if (leaving == "시정표" && !CommitSched(true)) { navLock = true; nav.SelectedIndex = curPage; navLock = false; return; }
                if (leaving == "기본 시간표") CommitTt();
                content.SuspendLayout();
                pages[curPage].Visible = false;
                content.ResumeLayout(false);
            }
            curPage = i;
            if (titles[i] == "기본 시간표") LoadTt();
            content.SuspendLayout();
            pages[i].Visible = true;
            pages[i].BringToFront();
            content.ResumeLayout(true);
        }

        // ---------- 화면 꾸미기 ----------
        static Label Sec(string t) { return new Label { Text = t, AutoSize = true, Font = Media.UiFont(11F, true), ForeColor = Ui.Ink, Margin = new Padding(2, 4, 3, 8) }; }
        static FlowLayoutPanel Row() { return new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(448, 0), Margin = new Padding(0, 0, 0, 4), BackColor = Color.Transparent }; }
        static Label Cap(string t) { return new Label { Text = t, AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(0, 9, 6, 3) }; }
        static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        GlassPanel Card(params Control[] rows)
        {
            var card = new GlassPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16, 12, 16, 12), Margin = new Padding(0, 0, 0, 12), Alpha = 135 };
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Dock = DockStyle.Fill, MinimumSize = new Size(448, 0) };
            flow.Controls.AddRange(rows);
            card.Controls.Add(flow);
            return card;
        }

        Swatch ColorBtn(string hex, Action<string> set)
        {
            var b = new Swatch { Value = Painter.ParseColor(hex), Margin = new Padding(0, 2, 12, 2), Editable = true, Width = 44 };
            new ToolTip().SetToolTip(b, "눌러서 원하는 색 고르기");
            b.Click += (o, e) =>
            {
                Color picked;
                if (ColorPicker.Pick(this, b.Value, out picked)) { b.Value = picked; set(Hex(picked)); Changed(); }
            };
            return b;
        }

        Slider MakeSlider(int min, int max, int val)
        {
            var t = new Slider { Minimum = min, Maximum = max, Width = 140, Height = 32, Margin = new Padding(0, 2, 14, 0) };
            t.Value = Math.Max(min, Math.Min(max, val));
            t.ValueChanged += (o, e) => Changed();
            return t;
        }

        ComboBox FontCombo(string selected, bool allowSame)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, MaxDropDownItems = 20, Margin = new Padding(0, 5, 10, 3) };
            var names = new List<string>();
            if (allowSame) names.Add("(날짜·안내 글꼴과 같게)");
            names.AddRange(BuiltinFonts.Names);   // 프로그램에 들어 있는 무료 글꼴
            try { using (var ifc = new InstalledFontCollection()) foreach (var fam in ifc.Families) if (!fam.Name.StartsWith("@")) names.Add(fam.Name); } catch { }
            if (!string.IsNullOrEmpty(selected) && !names.Contains(selected)) names.Insert(names.Count > 0 && allowSame ? 1 : 0, selected);
            cb.Items.AddRange(names.ToArray());
            cb.SelectedItem = string.IsNullOrEmpty(selected) && allowSame ? names[0] : selected;
            if (cb.SelectedIndex < 0 && cb.Items.Count > 0) cb.SelectedIndex = 0;
            return cb;
        }

        CheckBox Tg(string t, bool v) { var c = new Toggle { Text = t, Checked = v, AutoSize = true, Margin = new Padding(0, 2, 18, 2) }; c.CheckedChanged += (o, e) => Changed(); return c; }

        void BuildScreen()
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 0, 14, 12), BackColor = Color.Transparent };

            // 테마
            themeFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(448, 0), BackColor = Color.Transparent };
            foreach (var th in ThemePreset.All) themeFlow.Controls.Add(ThemeButton(th));
            p.Controls.Add(Card(Sec("테마"), new Label { Text = "고른 뒤 아래에서 하나씩 바꿀 수 있어요.", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(2, 0, 0, 8) }, themeFlow));

            // 시계 숫자
            rbSeg = new Chip { Text = "디지털 숫자", AutoSize = true, Checked = !R.DigitFont, Margin = new Padding(0, 2, 6, 2) };
            rbFontDigits = new Chip { Text = "글꼴 숫자", AutoSize = true, Checked = R.DigitFont, Margin = new Padding(0, 2, 6, 2) };
            rbSeg.CheckedChanged += (o, e) => { cbClockFont.Enabled = !rbSeg.Checked; Changed(); };
            var cr1 = Row(); cr1.Controls.Add(Cap("모양")); cr1.Controls.Add(rbSeg); cr1.Controls.Add(rbFontDigits);
            cbClockFont = FontCombo(R.ClockFontName, true);
            cbClockFont.Enabled = R.DigitFont;
            cbClockFont.SelectedIndexChanged += (o, e) => { if (loading) return; R.ClockFontName = cbClockFont.SelectedIndex == 0 ? "" : cbClockFont.SelectedItem as string; R.ClockFontFile = ""; UpdateFontFile(); Changed(); };
            var bClockFile = B("글꼴 파일…");
            bClockFile.Click += (o, e) => { var n = PickFontFile(); if (n != null) { R.ClockFontFile = n; R.DigitFont = true; loading = true; rbFontDigits.Checked = true; loading = false; UpdateFontFile(); Changed(); } };
            var cr2 = Row(); cr2.Controls.Add(Cap("글꼴")); cr2.Controls.Add(cbClockFont); cr2.Controls.Add(bClockFile);
            lClockFile = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(2, 0, 0, 6), MaximumSize = new Size(460, 0) };
            var cr3 = Row(); cr3.Controls.Add(Cap("색"));
            clockSwatches = new List<Swatch>();
            foreach (var c in Painter.Palette)
            {
                var sw = new Swatch { Value = Painter.ParseColor(c[1]), Width = 32, Height = 30, Margin = new Padding(0, 2, 4, 2), Tag = c[1] };
                new ToolTip().SetToolTip(sw, c[0]);
                sw.Click += (o, e) => { SetColor((string)((Swatch)o).Tag); Changed(); };
                clockSwatches.Add(sw); cr3.Controls.Add(sw);
            }
            bClockColor = ColorBtn(R.Color, v => SetColor(v));
            var cr3b = Row(); cr3b.Controls.Add(Cap("지금 색")); cr3b.Controls.Add(bClockColor);
            var bClockPick = B("원하는 색 직접 고르기…");
            bClockPick.Click += (o, e) => { Color pc2; if (ColorPicker.Pick(this, Painter.ParseColor(R.Color), out pc2)) { SetColor(Hex(pc2)); Changed(); } };
            cr3b.Controls.Add(bClockPick);
            var cr4 = Row();
            chkClockOutline = Tg("테두리", R.ClockOutline); cr4.Controls.Add(chkClockOutline);
            bClockOutlineColor = ColorBtn(R.ClockOutlineColor, v => R.ClockOutlineColor = v); cr4.Controls.Add(bClockOutlineColor);
            cr4.Controls.Add(Cap("두께")); tbClockOutline = MakeSlider(1, 12, R.ClockOutlineWidth); cr4.Controls.Add(tbClockOutline);
            var cr5 = Row(); chkClockBold = Tg("굵게", R.ClockBold); cr5.Controls.Add(chkClockBold);
            chkSec = Tg("초 보이기", R.ShowSeconds); cr5.Controls.Add(chkSec);
            p.Controls.Add(Card(Sec("시계 숫자"), cr1, cr2, lClockFile, cr3, cr3b, cr4, cr5));

            // 날짜·안내 글자
            cbFont = FontCombo(R.FontName, false);
            cbFont.SelectedIndexChanged += (o, e) => { if (loading) return; R.FontName = cbFont.SelectedItem as string ?? R.FontName; R.FontFile = ""; UpdateFontFile(); Changed(); };
            var bFontFile = B("글꼴 파일…");
            bFontFile.Click += (o, e) => { var n = PickFontFile(); if (n != null) { R.FontFile = n; UpdateFontFile(); Changed(); } };
            var tr1 = Row(); tr1.Controls.Add(Cap("글꼴")); tr1.Controls.Add(cbFont); tr1.Controls.Add(bFontFile);
            lFontFile = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(2, 0, 0, 6), MaximumSize = new Size(460, 0) };
            var tr2 = Row(); tr2.Controls.Add(Cap("색"));
            bSubColor = ColorBtn(Hex(Painter.SubOf(R)), v => R.SubColor = v);
            tr2.Controls.Add(bSubColor);
            var bSame = B("자동 (시계 색보다 살짝 어둡게)");
            bSame.Click += (o, e) => { R.SubColor = ""; bSubColor.Value = Painter.SubOf(R); Changed(); };
            tr2.Controls.Add(bSame);
            var tr3 = Row();
            chkOutline = Tg("테두리", R.Outline); tr3.Controls.Add(chkOutline);
            bOutlineColor = ColorBtn(R.OutlineColor, v => R.OutlineColor = v); tr3.Controls.Add(bOutlineColor);
            tr3.Controls.Add(Cap("두께")); tbOutline = MakeSlider(1, 12, R.OutlineWidth); tr3.Controls.Add(tbOutline);
            var tr4 = Row(); chkBold = Tg("굵게", R.Bold); tr4.Controls.Add(chkBold);
            chkDate = Tg("날짜 보이기", R.ShowDate); tr4.Controls.Add(chkDate);
            chkStatus = Tg("교시·남은 시간 보이기", R.ShowStatus); tr4.Controls.Add(chkStatus);
            p.Controls.Add(Card(Sec("날짜·안내 글자"), tr1, lFontFile, tr2, tr3, tr4));

            // 시계 아래 텍스트(메모)
            cbMemoFont = FontCombo(R.MemoFont, true);
            cbMemoFont.SelectedIndexChanged += (o, e) => { if (loading) return; R.MemoFont = cbMemoFont.SelectedIndex == 0 ? "" : cbMemoFont.SelectedItem as string; Changed(); };
            var mr1 = Row(); mr1.Controls.Add(Cap("글꼴")); mr1.Controls.Add(cbMemoFont);
            mr1.Controls.Add(Cap("크기")); slMemoSize = MakeSlider(12, 40, R.MemoSize); slMemoSize.Width = 120; mr1.Controls.Add(slMemoSize);
            var mr2 = Row(); mr2.Controls.Add(Cap("글자 색")); bMemoColor = ColorBtn(R.MemoColor, v => R.MemoColor = v); mr2.Controls.Add(bMemoColor);
            chkMemoBold = Tg("굵게", R.MemoBold); mr2.Controls.Add(chkMemoBold);
            chkMemoLine = Tg("글자 테두리", R.MemoOutline); mr2.Controls.Add(chkMemoLine);
            bMemoLine = ColorBtn(R.MemoOutlineColor, v => R.MemoOutlineColor = v); mr2.Controls.Add(bMemoLine);
            var mr3 = Row(); chkMemoBg = Tg("배경 상자", R.MemoBgOn); mr3.Controls.Add(chkMemoBg);
            bMemoBg = ColorBtn(R.MemoBgColor, v => R.MemoBgColor = v); mr3.Controls.Add(bMemoBg);
            mr3.Controls.Add(Cap("진하기")); slMemoAlpha = MakeSlider(20, 100, R.MemoBgOpacity); slMemoAlpha.Width = 120; mr3.Controls.Add(slMemoAlpha);
            p.Controls.Add(Card(Sec("시계 아래 텍스트"), new Label { Text = "위젯의 [텍스트]로 쓴 글의 모양이에요. 미리보기에서 바로 확인돼요.", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(2, 0, 0, 6) }, mr1, mr2, mr3));

            // 효과
            var er = Row();
            chkShadow = Tg("옅은 그림자", R.Shadow); er.Controls.Add(chkShadow);
            chkGlow = Tg("빛나기", R.Glow); er.Controls.Add(chkGlow);
            p.Controls.Add(Card(Sec("효과"), er));

            // 배경
            cbBgStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Margin = new Padding(0, 5, 14, 3) };
            cbBgStyle.Items.AddRange(new object[] { "투명 (글자만)", "단색 카드", "그라데이션 카드", "이미지 카드" });
            cbBgStyle.SelectedIndex = R.BgStyle;
            cbBgStyle.SelectedIndexChanged += (o, e) => Changed();
            var br1 = Row(); br1.Controls.Add(cbBgStyle);
            bBg1 = ColorBtn(R.BgColor, v => R.BgColor = v); bBg2 = ColorBtn(R.BgColor2, v => R.BgColor2 = v);
            br1.Controls.Add(Cap("색 1")); br1.Controls.Add(bBg1); br1.Controls.Add(Cap("색 2")); br1.Controls.Add(bBg2);
            var br2 = Row();
            bBgImg = B("배경 이미지…");
            bBgImg.Click += (o, e) =>
            {
                using (var od = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "위젯 배경 이미지" })
                    if (od.ShowDialog(this) == DialogResult.OK) { var n = Media.Import(od.FileName); if (n != null) { R.BgImage = n; loading = true; cbBgStyle.SelectedIndex = 3; loading = false; Changed(); } }
            };
            br2.Controls.Add(bBgImg);
            chkBorder = Tg("테두리 선", R.BgBorder); br2.Controls.Add(chkBorder);
            bBorderColor = ColorBtn(R.BgBorderColor, v => R.BgBorderColor = v); br2.Controls.Add(bBorderColor);
            var br3 = Row();
            br3.Controls.Add(Cap("진하기")); tbOpacity = MakeSlider(10, 100, R.BgOpacity); br3.Controls.Add(tbOpacity);
            br3.Controls.Add(Cap("둥글기")); tbRadius = MakeSlider(0, 40, R.Radius); br3.Controls.Add(tbRadius);
            p.Controls.Add(Card(Sec("배경"), br1, br2, br3));

            // 기타
            var vf = Row();
            chkTop = Tg("항상 다른 창 위에", R.TopMostOn); vf.Controls.Add(chkTop);
            chkLocked = Tg("잠금", R.Locked); vf.Controls.Add(chkLocked);
            chkAuto = Tg("윈도우 켤 때 자동 실행", R.AutoStart); vf.Controls.Add(chkAuto);
            chkReadable = new CheckBox { Visible = false };
            chkDigitFont = new CheckBox { Visible = false };
            var tf = Row();
            tf.Controls.Add(Cap("선생님 안내 보여 주는 시간"));
            nBanner = new NumericUpDown { Minimum = 0, Maximum = 600, Value = R.BannerSeconds, Width = 64, Margin = new Padding(0, 5, 4, 3) };
            tf.Controls.Add(nBanner); tf.Controls.Add(Cap("초 (0 = 누를 때까지)"));
            var tf2 = Row();
            tf2.Controls.Add(Cap("타이머 빠른 시간"));
            txtPresets = new TextBox { Width = 130, Text = string.Join(", ", R.Presets), Margin = new Padding(0, 5, 4, 3) };
            tf2.Controls.Add(txtPresets); tf2.Controls.Add(Cap("분, 쉼표로 구분"));
            var rf = Row();
            var bPos = B("크기·위치 처음으로");
            lPosNote = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(6, 9, 3, 3) };
            bPos.Click += (o, e) => { R.X = int.MinValue; R.Y = int.MinValue; R.Scale = 1f; lPosNote.Text = "저장하면 오른쪽 위로 돌아가요."; };
            var bAll = B("모든 설정 초기화");
            bAll.Click += (o, e) =>
            {
                if (MessageBox.Show(this, "시정표, 시간표, 명단, 안내 문구까지 모두 처음 상태로 돌아가요. 계속할까요?", "RamClock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                { R = Settings.Defaults(); DialogResult = DialogResult.OK; Close(); }
            };
            rf.Controls.AddRange(new Control[] { bPos, bAll, lPosNote });
            p.Controls.Add(Card(Sec("기타"), vf, tf, tf2, rf, new Label { Text = "저장 위치: " + Settings.Dir, AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(460, 0), Margin = new Padding(2, 8, 0, 0) }));

            // 오른쪽 미리보기
            var right = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = new Padding(10, 0, 0, 0), Alpha = 135 };
            var rt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
            rt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rt.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var rh = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
            rh.Controls.Add(new Label { Text = "미리보기", AutoSize = true, Font = Media.UiFont(11F, true), Margin = new Padding(0, 8, 12, 3) });
            cbBack = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(0, 5, 0, 3) };
            cbBack.Items.AddRange(new object[] { "밝은 수업 화면", "어두운 화면", "칠판", "사진 배경" });
            cbBack.SelectedIndex = 0;
            cbBack.SelectedIndexChanged += (o, e) => RenderPreview();
            rh.Controls.Add(cbBack);
            rt.Controls.Add(rh);
            preview = new PaintBox { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Cursor = Cursors.Default };
            preview.Resize += (o, e) => RenderPreview();
            preview.Paint += (o, e) => { if (previewBmp != null) e.Graphics.DrawImageUnscaled(previewBmp, 0, 0); else RenderPreview(); };
            rt.Controls.Add(preview);
            right.Controls.Add(rt);

            var split = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 530));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.Dock = DockStyle.Fill;
            p.AutoScroll = false; p.HorizontalScroll.Enabled = false; p.HorizontalScroll.Visible = false; p.HorizontalScroll.Maximum = 0; p.AutoScroll = true;
            split.Controls.Add(p, 0, 0); split.Controls.Add(right, 1, 0);

            previewTimer = new Timer { Interval = 120 };
            previewTimer.Tick += (o, e) => { previewTimer.Stop(); RenderPreview(); };

            Page("디자인", new Control[] { split }, new float[] { 100 }, false);
            SetColor(R.Color);
            UpdateFontFile();
        }

        Control ThemeButton(ThemePreset th)
        {
            var b = new PaintBox { Width = 134, Height = 82, Margin = new Padding(0, 0, 10, 10) };
            b.Paint += (o, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var card = new RectangleF(2, 2, b.Width - 5, b.Height - 26);
                using (var path = Ui.Round(card, 12))
                {
                    if (th.BgStyle == 0) using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(card, Color.FromArgb(96, 112, 150), Color.FromArgb(160, 176, 210), 30f)) g.FillPath(br, path);
                    else if (th.BgStyle == 2) using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(card, Painter.ParseColor(th.Bg1), Painter.ParseColor(th.Bg2 ?? th.Bg1), 60f)) g.FillPath(br, path);
                    else using (var br = new SolidBrush(Painter.ParseColor(th.Bg1))) g.FillPath(br, path);
                    if (th.BgBorder) using (var pen = new Pen(Painter.ParseColor(th.Border), 1.5f)) g.DrawPath(pen, path);
                }
                using (var tp = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    tp.AddString("09:45", Painter.UiFam, (int)FontStyle.Bold, 24, new PointF(0, 0), StringFormat.GenericTypographic);
                    var bb = tp.GetBounds();
                    using (var m = new System.Drawing.Drawing2D.Matrix()) { m.Translate(card.X + (card.Width - bb.Width) / 2 - bb.X, card.Y + (card.Height - bb.Height) / 2 - bb.Y); tp.Transform(m); }
                    var col = Painter.ParseColor(th.Color);
                    if (th.Glow) using (var pen = new Pen(Color.FromArgb(70, col), 6) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round }) g.DrawPath(pen, tp);
                    if (th.Outline) using (var pen = new Pen(Painter.ParseColor(th.OutlineColor ?? "#FFFFFF"), 3) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round }) g.DrawPath(pen, tp);
                    using (var br = new SolidBrush(col)) g.FillPath(br, tp);
                }
                bool sel = R.Theme == th.Name;
                if (sel) using (var path = Ui.Round(new RectangleF(1, 1, b.Width - 3, b.Height - 24), 13)) using (var pen = new Pen(Ui.Accent, 2.5f)) g.DrawPath(pen, path);
                using (var f = Media.UiFont(9F, sel))
                    TextRenderer.DrawText(g, th.Name, f, new Rectangle(0, b.Height - 22, b.Width, 20), sel ? Ui.Accent : Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            b.Click += (o, e) =>
            {
                th.Apply(R);
                SyncScreen();
                foreach (Control c in themeFlow.Controls) c.Invalidate();
                RenderPreview();
            };
            return b;
        }

        void SyncScreen()
        {
            loading = true;
            rbSeg.Checked = !R.DigitFont; rbFontDigits.Checked = R.DigitFont; cbClockFont.Enabled = R.DigitFont;
            chkClockOutline.Checked = R.ClockOutline; bClockOutlineColor.Value = Painter.ParseColor(R.ClockOutlineColor); tbClockOutline.Value = R.ClockOutlineWidth; chkClockBold.Checked = R.ClockBold;
            chkOutline.Checked = R.Outline; chkShadow.Checked = R.Shadow; chkGlow.Checked = R.Glow;
            chkBorder.Checked = R.BgBorder; chkBold.Checked = R.Bold;
            tbOutline.Value = R.OutlineWidth; tbOpacity.Value = Math.Max(10, R.BgOpacity); tbRadius.Value = Math.Min(40, R.Radius);
            cbBgStyle.SelectedIndex = R.BgStyle;
            bOutlineColor.Value = Painter.ParseColor(R.OutlineColor); bBg1.Value = Painter.ParseColor(R.BgColor); bBg2.Value = Painter.ParseColor(R.BgColor2);
            bBorderColor.Value = Painter.ParseColor(R.BgBorderColor);
            bSubColor.Value = Painter.SubOf(R);
            if (slMemoSize != null)
            {
                slMemoSize.Value = Math.Max(12, Math.Min(40, R.MemoSize)); chkMemoBold.Checked = R.MemoBold; chkMemoLine.Checked = R.MemoOutline;
                chkMemoBg.Checked = R.MemoBgOn; slMemoAlpha.Value = Math.Max(20, Math.Min(100, R.MemoBgOpacity));
                bMemoColor.Value = Painter.ParseColor(R.MemoColor); bMemoBg.Value = Painter.ParseColor(R.MemoBgColor); bMemoLine.Value = Painter.ParseColor(R.MemoOutlineColor);
            }
            SetColor(R.Color);
            loading = false;
        }

        void Changed()
        {
            if (loading || chkSec == null || chkOutline == null || cbBgStyle == null || tbRadius == null || chkTop == null || rbSeg == null) return;
            R.DigitFont = rbFontDigits.Checked;
            R.ClockOutline = chkClockOutline.Checked; R.ClockOutlineWidth = tbClockOutline.Value; R.ClockBold = chkClockBold.Checked;
            R.Outline = chkOutline.Checked; R.Shadow = chkShadow.Checked; R.Glow = chkGlow.Checked;
            R.Bold = chkBold.Checked; R.BgBorder = chkBorder.Checked;
            R.OutlineWidth = tbOutline.Value; R.BgOpacity = tbOpacity.Value; R.Radius = tbRadius.Value;
            R.BgStyle = cbBgStyle.SelectedIndex < 0 ? 0 : cbBgStyle.SelectedIndex;
            R.ShowDate = chkDate.Checked; R.ShowStatus = chkStatus.Checked; R.ShowSeconds = chkSec.Checked;
            if (slMemoSize != null)
            {
                R.MemoSize = slMemoSize.Value; R.MemoBold = chkMemoBold.Checked; R.MemoOutline = chkMemoLine.Checked;
                R.MemoBgOn = chkMemoBg.Checked; R.MemoBgOpacity = slMemoAlpha.Value;
            }
            R.Theme = "";
            foreach (Control c in themeFlow.Controls) c.Invalidate();
            RenderPreview();
        }

        string PickFontFile()
        {
            using (var od = new OpenFileDialog { Filter = "글꼴 파일 (TTF 권장)|*.ttf;*.otf;*.ttc", Title = "글꼴 파일 불러오기" })
            {
                if (od.ShowDialog(this) != DialogResult.OK) return null;
                var n = Media.Import(od.FileName);
                if (n == null) return null;
                if (Painter.FileFam(n) == null)
                {
                    MessageBox.Show(this, "이 글꼴 파일은 쓸 수 없어요.\n같은 글꼴의 TTF 파일을 골라 주세요. (OTF 중 일부는 지원되지 않아요)", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                return n;
            }
        }

        void UpdateFontFile()
        {
            if (lFontFile == null || lClockFile == null) return;
            var ff = Painter.FileFam(R.FontFile);
            lFontFile.Text = ff != null ? "불러온 글꼴: " + ff.Name : "★ 글꼴은 프로그램에 들어 있어서 어느 컴퓨터에서나 똑같이 보여요. 가진 글꼴 파일(TTF)도 [글꼴 파일…]로 불러올 수 있어요.";
            var cf = Painter.FileFam(R.ClockFontFile);
            lClockFile.Text = cf != null ? "불러온 글꼴: " + cf.Name : (R.DigitFont ? "" : "글꼴 숫자를 고르면 원하는 글꼴로 시계를 보여 줘요.");
        }

        static void Backdrop(Graphics g, Rectangle r, int kind)
        {
            if (kind == 1) { using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(20, 27, 45), Color.FromArgb(44, 56, 86), 60f)) g.FillRectangle(b, r); return; }
            if (kind == 2)
            {
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(46, 84, 64), Color.FromArgb(32, 64, 48), 90f)) g.FillRectangle(b, r);
                using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 3)) { g.DrawLine(pen, r.X + 30, r.Bottom - 60, r.X + 180, r.Bottom - 70); g.DrawLine(pen, r.X + 40, r.Bottom - 40, r.X + 140, r.Bottom - 44); }
                return;
            }
            if (kind == 3)
            {
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(120, 180, 235), Color.FromArgb(250, 205, 160), 90f)) g.FillRectangle(b, r);
                using (var b = new SolidBrush(Color.FromArgb(90, 150, 110))) g.FillEllipse(b, r.X - r.Width / 4, r.Bottom - r.Height / 3, r.Width * 1.5f, r.Height / 1.5f);
                return;
            }
            g.Clear(Color.White);
            using (var b = new SolidBrush(Color.FromArgb(76, 99, 255))) g.FillRectangle(b, r.X, r.Y, r.Width, 34);
            using (var b = new SolidBrush(Color.FromArgb(226, 232, 242)))
                for (int i = 0; i < 6; i++) g.FillRectangle(b, r.X + 24, r.Y + 60 + i * 30, r.Width * (i % 2 == 0 ? 0.62f : 0.45f), 12);
        }

        void RenderPreview()
        {
            try { RenderPreview2(); } catch (Exception ex) { Program.Log(ex); }
        }

        void RenderPreview2()
        {
            if (preview == null || preview.Width < 20 || preview.Height < 20) return;
            var bmp = new Bitmap(preview.Width, preview.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var area = new Rectangle(0, 0, bmp.Width, bmp.Height);
                using (var clip = Ui.Round(new RectangleF(0, 0, bmp.Width - 1, bmp.Height - 1), 14))
                {
                    g.SetClip(clip);
                    Backdrop(g, area, cbBack.SelectedIndex);
                    float keep = R.Scale; R.Scale = 1f;
                    string keepMemo = R.Memo;
                    if (string.IsNullOrWhiteSpace(R.Memo)) R.Memo = "시계 아래 텍스트 미리보기";
                    Composition c = null;
                    try { c = Painter.Compose(R, new HudState(), new DateTime(2026, 10, 8, 9, 45, 52), 1f, 0, 0, new Rectangle(-5000, -5000, 10000, 10000)); }
                    catch { }
                    R.Scale = keep; R.Memo = keepMemo;
                    if (c != null)
                    {
                        float sc = Math.Min(1.4f, Math.Min(bmp.Width * 0.9f / c.Bmp.Width, bmp.Height * 0.8f / c.Bmp.Height));
                        float w = c.Bmp.Width * sc, h = c.Bmp.Height * sc;
                        g.DrawImage(c.Bmp, (bmp.Width - w) / 2, (bmp.Height - h) / 2, w, h);
                        c.Bmp.Dispose();
                    }
                    g.ResetClip();
                }
            }
            var old = previewBmp; previewBmp = bmp; if (old != null) old.Dispose();
            preview.Invalidate();
        }

        void SetColor(string hex)
        {
            R.Color = hex.ToUpperInvariant();
            if (clockSwatches != null) foreach (var sw in clockSwatches) { sw.Selected = string.Equals(sw.Tag as string, R.Color, StringComparison.OrdinalIgnoreCase); sw.Invalidate(); }
            if (bClockColor != null) bClockColor.Value = Painter.ParseColor(R.Color);
            if (bSubColor != null && string.IsNullOrEmpty(R.SubColor)) bSubColor.Value = Painter.SubOf(R);
            if (!loading && previewTimer != null) { R.Theme = ""; if (themeFlow != null) foreach (Control c in themeFlow.Controls) c.Invalidate(); }
        }

        void CommitScreen()
        {
            Changed();
            R.TopMostOn = chkTop.Checked; R.ShowDate = chkDate.Checked; R.ShowStatus = chkStatus.Checked;
            R.Locked = chkLocked.Checked; R.AutoStart = chkAuto.Checked;
            R.BannerSeconds = (int)nBanner.Value;
            var list = new List<int>();
            foreach (var part in txtPresets.Text.Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n; if (int.TryParse(part.Replace("분", ""), out n) && n > 0 && n <= 180 && !list.Contains(n)) list.Add(n);
                if (list.Count >= 6) break;
            }
            R.Presets = list.Count > 0 ? list : new List<int> { 3, 5, 10 };
        }

        // ---------- 시정표 ----------
        void BuildSched()
        {
            var top = Flow();
            cbSchedDay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(3, 5, 10, 3) };
            cbSchedDay.Items.AddRange(new object[] { "기본 시정표", "월요일", "화요일", "수요일", "목요일", "금요일" });
            chkOverride = new Toggle { Text = "이 요일만 별도 시정표 사용", AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
            top.Controls.Add(cbSchedDay); top.Controls.Add(chkOverride);

            gSched = NewGrid();
            gSched.Columns.Add(ComboCol("구분", 30, TypeKo));
            gSched.Columns.Add(TextCol("이름", 16, true));
            gSched.Columns.Add(TextCol("시작 (예: 09:00)", 27));
            gSched.Columns.Add(TextCol("끝 (예: 09:40)", 27));
            AddDeleteCol(gSched, () => Renumber());
            gSched.CellValueChanged += (o, e) => { if (!loading && e.ColumnIndex == 0) Renumber(); };
            // 0945, 945, 9시45분처럼 써도 09:45로 바꿔 주기
            gSched.CellEndEdit += (o, e) =>
            {
                if (e.ColumnIndex != 2 && e.ColumnIndex != 3 || e.RowIndex < 0) return;
                var c = gSched.Rows[e.RowIndex].Cells[e.ColumnIndex];
                int mm = Sched.ToMin(c.Value as string);
                if (mm >= 0) { c.Value = Sched.FromMin(mm); c.Style.BackColor = Color.Empty; }
                else if (!string.IsNullOrWhiteSpace(c.Value as string)) c.Style.BackColor = Color.MistyRose;
            };

            var bar = Flow();
            bAdd = B("+ 추가"); bUp = Arrow("▲", "고른 줄을 위로"); bDown = Arrow("▼", "고른 줄을 아래로"); bReset = B("처음 시정표로 되돌리기");
            bar.Controls.AddRange(new Control[] { bAdd, bUp, bDown, bReset });
            lSchedNote = Note("");

            bAdd.Click += (o, e) => AddSchedRow();
            bUp.Click += (o, e) => MoveRow(-1);
            bDown.Click += (o, e) => MoveRow(1);
            bReset.Click += (o, e) => { if (AskReset(schedDay == 0 ? "프로그램에 처음 들어 있던 시정표로 되돌려요." : "이 요일 시정표를 기본 시정표와 똑같이 되돌려요.")) FillSchedGrid(schedDay == 0 ? Settings.DefaultSchedule() : ClonePeriods(R.Schedule)); };
            cbSchedDay.SelectedIndexChanged += (o, e) =>
            {
                if (loading) return;
                if (!CommitSched(true)) { loading = true; cbSchedDay.SelectedIndex = schedDay; loading = false; return; }
                schedDay = cbSchedDay.SelectedIndex; LoadSched();
            };
            chkOverride.CheckedChanged += (o, e) =>
            {
                if (loading || schedDay == 0) return;
                string key = schedDay.ToString();
                if (chkOverride.Checked)
                {
                    if (!R.OverrideDays.Contains(schedDay)) R.OverrideDays.Add(schedDay);
                    if (!R.DaySchedules.ContainsKey(key) || R.DaySchedules[key] == null || R.DaySchedules[key].Count == 0)
                        R.DaySchedules[key] = ClonePeriods(R.Schedule);
                }
                else R.OverrideDays.Remove(schedDay);
                LoadSched();
            };

            Page("시정표", new Control[] { top, gSched, bar, lSchedNote }, new float[] { 0, 100, 0, 0 });
            loading = true; cbSchedDay.SelectedIndex = 0; loading = false;
            LoadSched();
        }

        static List<Period> ClonePeriods(List<Period> l) { var r = new List<Period>(); foreach (var p in l) r.Add(p.Clone()); return r; }
        bool SchedEditable { get { return schedDay == 0 || R.OverrideDays.Contains(schedDay); } }
        List<Period> SchedTarget()
        {
            if (schedDay == 0) return R.Schedule;
            List<Period> l;
            if (R.OverrideDays.Contains(schedDay) && R.DaySchedules.TryGetValue(schedDay.ToString(), out l)) return l;
            return R.Schedule;
        }

        void LoadSched()
        {
            loading = true;
            chkOverride.Visible = schedDay > 0;
            chkOverride.Checked = schedDay > 0 && R.OverrideDays.Contains(schedDay);
            loading = false;
            FillSchedGrid(SchedTarget());
            bool ed = SchedEditable;
            gSched.ReadOnly = !ed;
            gSched.DefaultCellStyle.ForeColor = ed ? SystemColors.ControlText : Color.Gray;
            foreach (var b in new[] { bAdd, bUp, bDown, bReset }) b.Enabled = ed;
            bReset.Text = schedDay == 0 ? "처음 시정표로 되돌리기" : "기본 시정표와 똑같이";
            lSchedNote.Text = !ed ? DayKo[schedDay] + "요일은 지금 기본 시정표를 따라요. 위 칸을 체크하면 이 요일만 따로 정할 수 있어요. (예: 수요일 5교시까지)"
                                 : "교시, 쉬는 시간, 점심시간을 줄마다 정해요. 시간은 24시간 형식(오후 1시 = 13:00)으로 적고, 교시 번호는 순서대로 자동으로 붙어요.";
        }

        void FillSchedGrid(List<Period> list)
        {
            loading = true;
            gSched.Rows.Clear();
            foreach (var p in list) gSched.Rows.Add(Ko(p.Type), "", p.Start, p.End);
            loading = false;
            Renumber();
        }

        void Renumber()
        {
            int n = 0;
            foreach (DataGridViewRow r in gSched.Rows)
            {
                string t = r.Cells[0].Value as string;
                r.Cells[1].Value = t == "교시" ? (++n) + "교시" : t;
            }
        }

        static int CurRow(DataGridView g) { return g.CurrentCell != null ? g.CurrentCell.RowIndex : (g.Rows.Count > 0 ? g.Rows.Count - 1 : -1); }

        void AddSchedRow()
        {
            gSched.EndEdit();
            string type = "교시", start = "09:00";
            if (gSched.Rows.Count > 0)
            {
                var last = gSched.Rows[gSched.Rows.Count - 1];
                type = (last.Cells[0].Value as string) == "교시" ? "쉬는 시간" : "교시";
                int e = Sched.ToMin(last.Cells[3].Value as string);
                if (e >= 0) start = Sched.FromMin(e);
            }
            string end = Sched.FromMin(Sched.ToMin(start) + (type == "교시" ? 40 : 10));
            int i = gSched.Rows.Add(type, "", start, end);
            Renumber();
            gSched.CurrentCell = gSched.Rows[i].Cells[2];
        }

        void MoveRow(int d)
        {
            gSched.EndEdit();
            int r = CurRow(gSched), t = r + d;
            if (r < 0 || t < 0 || t >= gSched.Rows.Count) return;
            for (int c = 0; c < 4; c++)
            {
                var v = gSched.Rows[r].Cells[c].Value;
                gSched.Rows[r].Cells[c].Value = gSched.Rows[t].Cells[c].Value;
                gSched.Rows[t].Cells[c].Value = v;
            }
            Renumber();
            gSched.CurrentCell = gSched.Rows[t].Cells[gSched.CurrentCell == null ? 0 : gSched.CurrentCell.ColumnIndex];
        }

        bool CommitSched(bool warn)
        {
            gSched.EndEdit();
            if (!SchedEditable) return true;
            var list = new List<Period>();
            bool ok = true;
            foreach (DataGridViewRow r in gSched.Rows)
            {
                string a = (r.Cells[2].Value as string ?? "").Trim(), b = (r.Cells[3].Value as string ?? "").Trim();
                int ma = Sched.ToMin(a), mb = Sched.ToMin(b);
                bool bad = ma < 0 || mb < 0 || mb <= ma;
                r.Cells[2].Style.BackColor = r.Cells[3].Style.BackColor = bad ? Color.MistyRose : Color.Empty;
                if (bad) { ok = false; continue; }
                list.Add(new Period(En(r.Cells[0].Value), Sched.FromMin(ma), Sched.FromMin(mb)));
            }
            if (!ok)
            {
                if (warn) MessageBox.Show(this, "빨간 칸의 시간을 확인해 주세요.\n09:00처럼 적고, 끝 시각은 시작보다 늦어야 해요.", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var target = SchedTarget();
            target.Clear(); target.AddRange(list);
            return true;
        }

        // ---------- 기본 시간표 ----------
        void BuildTt()
        {
            var top = Flow();
            cbTtDay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Margin = new Padding(3, 5, 10, 3) };
            cbTtDay.Items.AddRange(new object[] { "월요일", "화요일", "수요일", "목요일", "금요일" });
            cbTtDay.SelectedIndex = 0;
            cbTtDay.SelectedIndexChanged += (o, e) => { CommitTt(); ttDay = cbTtDay.SelectedIndex + 1; LoadTt(); };
            top.Controls.Add(cbTtDay);
            // 요일마다 몇 교시까지 (예: 수요일 4교시)
            top.Controls.Add(new Label { Text = "이 요일 수업은", AutoSize = true, Margin = new Padding(6, 9, 6, 3) });
            cbTtCount = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(0, 5, 10, 3) };
            cbTtCount.Items.Add("시정표대로 전부");
            for (int i = 1; i <= 8; i++) cbTtCount.Items.Add(i + "교시까지");
            cbTtCount.SelectedIndexChanged += (o, e) =>
            {
                if (ttCountLoading) return;
                CommitTt();
                if (cbTtCount.SelectedIndex <= 0) R.DayPeriods.Remove(ttDay.ToString()); else R.DayPeriods[ttDay.ToString()] = cbTtCount.SelectedIndex;
                LoadTt();
            };
            top.Controls.Add(cbTtCount);
            gTt = NewGrid();
            gTt.Columns.Add(TextCol("교시", 18, true));
            gTt.Columns.Add(TextCol("과목", 60));
            gTt.Columns.Add(CheckCol("이동 수업", 22));
            var note = Note("주간학습안내를 넣지 않은 주에 쓰는 기본 시간표예요. 매주 바뀌는 과목은 [이번 주 시간표]에서 주간학습안내(PDF·한글)를 넣으면 그 주에만 적용돼요. 처음 들어 있는 과목은 예시예요.");

            // 시간표 안에서 [기본 시간표 | 이번 주 시간표] 나누기
            var basic = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Margin = new Padding(0) };
            basic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            basic.RowStyles.Add(new RowStyle(SizeType.AutoSize)); basic.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); basic.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.Dock = DockStyle.Fill; gTt.Dock = DockStyle.Fill; note.Dock = DockStyle.Fill;
            basic.Controls.Add(top, 0, 0); basic.Controls.Add(gTt, 0, 1); basic.Controls.Add(note, 0, 2);
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
            host.Controls.Add(basic);
            var sw = Flow();
            var cBasic = new Chip { Text = "기본 시간표", AutoSize = true, Checked = true, Margin = new Padding(0, 0, 6, 8) };
            var cWeek = new Chip { Text = "이번 주 시간표 (주간학습안내)", AutoSize = true, Margin = new Padding(0, 0, 6, 8) };
            sw.Controls.Add(cBasic); sw.Controls.Add(cWeek);
            WeekForm wk = null;
            cWeek.CheckedChanged += (o, e) =>
            {
                if (!cWeek.Checked) return;
                CommitTt();
                if (wk == null || wk.IsDisposed)
                {
                    var d = DateTime.Today;
                    if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(2);
                    wk = new WeekForm(R, null, d) { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
                    host.Controls.Add(wk);
                }
                basic.Visible = false; wk.Visible = true; wk.BringToFront();
            };
            cBasic.CheckedChanged += (o, e) =>
            {
                if (!cBasic.Checked) return;
                if (wk != null && !wk.IsDisposed) wk.Visible = false;
                basic.Visible = true; LoadTt();
            };
            showWeek = () => cWeek.Checked = true;
            Page("시간표", new Control[] { sw, host }, new float[] { 0, 100 });
        }

        ComboBox cbTtCount; bool ttCountLoading;
        void LoadTt()
        {
            int n = 0;
            foreach (var sl in Sched.Slots(R, ttDay)) if (sl.Type == "class") n++;
            int lim; if (!R.DayPeriods.TryGetValue(ttDay.ToString(), out lim)) lim = 0;
            if (cbTtCount != null) { ttCountLoading = true; cbTtCount.SelectedIndex = Math.Max(0, Math.Min(8, lim)); ttCountLoading = false; }
            if (lim > 0 && lim < n) n = lim;
            List<Subj> l;
            if (!R.Timetable.TryGetValue(ttDay.ToString(), out l) || l == null) { l = new List<Subj>(); R.Timetable[ttDay.ToString()] = l; }
            gTt.Rows.Clear();
            for (int i = 0; i < n; i++)
            {
                var s = i < l.Count && l[i] != null ? l[i] : new Subj("", false);
                gTt.Rows.Add((i + 1) + "교시", s.Name, s.Move);
            }
            ttLoaded = true;
        }

        void CommitTt()
        {
            if (!ttLoaded) return;
            gTt.EndEdit();
            List<Subj> l = R.Timetable[ttDay.ToString()];
            for (int i = 0; i < gTt.Rows.Count; i++)
            {
                var s = new Subj((gTt.Rows[i].Cells[1].Value as string ?? "").Trim(), gTt.Rows[i].Cells[2].Value is bool && (bool)gTt.Rows[i].Cells[2].Value);
                if (i < l.Count) l[i] = s; else l.Add(s);
            }
        }

        // ---------- 자동 음성 안내 ----------
        void BuildRules()
        {
            var top = Flow();
            chkVoice = new Toggle { Text = "음성 안내 사용", AutoSize = true, Checked = R.Voice, Margin = new Padding(3, 5, 16, 3) };
            chkSfx = new Toggle { Text = "효과음(딩동) 사용", AutoSize = true, Checked = R.Sfx, Margin = new Padding(3, 5, 16, 3) };
            top.Controls.Add(chkVoice); top.Controls.Add(chkSfx);
            // 목소리 고르기 · 빠르기
            var vr = Flow();
            vr.Controls.Add(new Label { Text = "목소리", AutoSize = true, Margin = new Padding(3, 9, 6, 3) });
            cbVoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290, Margin = new Padding(0, 5, 14, 3) };
            cbVoice.Items.Add("기본 (한국어 목소리)");
            foreach (var n in Audio.VoiceNames()) cbVoice.Items.Add(n);
            cbVoice.SelectedItem = string.IsNullOrEmpty(R.VoiceName) || !cbVoice.Items.Contains(R.VoiceName) ? cbVoice.Items[0] : R.VoiceName;
            vr.Controls.Add(cbVoice);
            vr.Controls.Add(new Label { Text = "빠르기", AutoSize = true, Margin = new Padding(3, 9, 6, 3) });
            slRate = new Slider { Minimum = -5, Maximum = 5, Width = 130, Height = 32, Margin = new Padding(0, 2, 14, 0) }; slRate.Value = Math.Max(-5, Math.Min(5, R.VoiceRate));
            vr.Controls.Add(slRate);
            var bHear = B("▶ 들어보기");
            bHear.Click += (o, e) =>
            {
                string vn = cbVoice.SelectedIndex <= 0 ? "" : cbVoice.SelectedItem as string;
                Audio.Stop();
                bHear.Enabled = false; bHear.Text = "읽는 중…";
                Audio.TestVoice(vn, slRate.Value, "안녕하세요. 1교시 국어 시간입니다.", err =>
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            bHear.Enabled = true; bHear.Text = "▶ 들어보기";
                            if (err != null) MessageBox.Show(this, "이 목소리는 지금 소리를 낼 수 없어요.\n\n" + vn + "\n이유: " + err + "\n\n다른 목소리를 골라 주세요.", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }));
                    }
                    catch { }
                });
            };
            vr.Controls.Add(bHear);
            var vnote = Note("목소리를 더 넣으려면 윈도우 설정 → 시간 및 언어 → 음성 → [음성 추가]에서 한국어 목소리를 받아요. 받은 목소리는 이 목록에 바로 나와요.");
            var vbox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            vbox.Controls.Add(top); vbox.Controls.Add(vr); vbox.Controls.Add(vnote);
            top = vbox;
            if (!Audio.HasKoreanVoice)
                top.Controls.Add(new Label { Text = "컴퓨터에 한국어 음성이 없어요. 녹음이나 소리 파일을 쓰거나, 윈도우 설정 > 시간 및 언어 > 음성에서 추가해 주세요.", AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(3, 7, 3, 3), MaximumSize = new Size(860, 0) });

            gRules = NewGrid();
            gRules.Columns.Add(CheckCol("사용", 8));
            gRules.Columns.Add(TextCol("언제", 18, true));
            gRules.Columns.Add(ComboCol("반복", 9, "1회", "2회", "3회"));
            gRules.Columns.Add(TextCol("안내 문구 (글자 읽기일 때 읽어요)", 46));
            gRules.Columns.Add(TextCol("소리", 18, true));
            gRules.Columns.Add(BtnCol("소리 바꾸기", 12));
            gRules.Columns.Add(BtnCol("▶ 듣기", 9)); gRules.Columns[gRules.Columns.Count - 1].Name = "test";
            gRules.Columns[gRules.Columns.Count - 2].Name = "sound";
            for (int i = 0; i < Settings.RuleKeys.Length; i++)
            {
                var r = R.Rules[Settings.RuleKeys[i]];
                int idx = gRules.Rows.Add(r.On, Settings.RuleNames[i], Math.Max(1, Math.Min(3, r.Times)) + "회", r.Text, SoundText(r.Sound));
                gRules.Rows[idx].Tag = r.Sound ?? "";
            }
            gRules.CellContentClick += (o, e) =>
            {
                if (e.RowIndex < 0) return;
                gRules.EndEdit();
                var row = gRules.Rows[e.RowIndex];
                if (!(gRules.Columns[e.ColumnIndex] is DataGridViewButtonColumn)) return;   // 글자 칸을 누를 때는 아무 소리 없이 고치기만
                if (gRules.Columns[e.ColumnIndex].Name == "sound")
                {
                    using (var d = new SoundDialog(row.Cells[1].Value as string, row.Tag as string, Sched.Fill(row.Cells[3].Value as string, 1, "국어")))
                        if (d.ShowDialog(this) == DialogResult.OK) { row.Tag = d.Result; row.Cells[4].Value = SoundText(d.Result); }
                }
                else if (gRules.Columns[e.ColumnIndex].Name == "test")
                    Audio.Announce(Sched.Fill(row.Cells[3].Value as string, 1, "국어"), row.Tag as string, Times(row.Cells[2].Value), true, true);
            };
            var note = Note("{n}은 교시 번호, {subj}는 과목 이름으로 바뀌어요. [소리 바꾸기]에서 소리 파일(mp3·wav)을 넣거나 마이크로 직접 녹음하면 글자 읽기 대신 그 소리가 나와요.");
            Page("자동 음성 안내", new Control[] { top, gRules, note }, new float[] { 0, 100, 0 });
        }

        static int Times(object v) { var s = v as string ?? "1"; int n; return int.TryParse(s.Replace("회", ""), out n) ? n : 1; }

        // ---------- 선생님 안내 ----------
        void BuildMsgs()
        {
            gMsgs = NewGrid();
            gMsgs.Columns.Add(TextCol("버튼 이름", 15));
            gMsgs.Columns.Add(TextCol("안내 문구", 36));
            gMsgs.Columns.Add(TextCol("소리", 14, true));
            gMsgs.Columns.Add(TextCol("이미지", 14, true));
            gMsgs.Columns.Add(BtnCol("소리…", 9));
            gMsgs.Columns.Add(BtnCol("이미지…", 10));
            gMsgs.Columns.Add(BtnCol("▶ 듣기", 9)); gMsgs.Columns[gMsgs.Columns.Count - 1].Name = "test";
            gMsgs.Columns[gMsgs.Columns.Count - 2].Name = "image"; gMsgs.Columns[gMsgs.Columns.Count - 3].Name = "sound";
            AddDeleteCol(gMsgs, null);
            foreach (var m in R.Messages) AddMsgRow(m.Label, m.Text, m.Sound, m.Image);
            gMsgs.CellContentClick += (o, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= gMsgs.Rows.Count) return;
                string cn = gMsgs.Columns[e.ColumnIndex].Name;
                if (cn != "sound" && cn != "image" && cn != "test") return;   // 글자 칸은 고치기만
                gMsgs.EndEdit();
                var row = gMsgs.Rows[e.RowIndex]; var tag = (MsgTag)row.Tag;
                string label = row.Cells[0].Value as string ?? "";
                if (cn == "sound")
                {
                    using (var d = new SoundDialog(label, tag.Sound, row.Cells[1].Value as string))
                        if (d.ShowDialog(this) == DialogResult.OK) { tag.Sound = d.Result; row.Cells[2].Value = SoundText(d.Result); }
                }
                else if (cn == "image")
                {
                    using (var d = new ImageDialog(label, tag.Image))
                        if (d.ShowDialog(this) == DialogResult.OK) { tag.Image = d.Result; row.Cells[3].Value = ImageText(d.Result); }
                }
                else if (cn == "test") Audio.Announce(row.Cells[1].Value as string, tag.Sound, 1, true, true);
            };
            var bar = Flow();
            var bA = B("+ 안내 버튼 추가"); var bMu = Arrow("▲", "고른 줄을 위로"); var bMd = Arrow("▼", "고른 줄을 아래로"); var bR = B("처음 안내로 되돌리기");
            bA.Click += (o, e) => { int i = AddMsgRow("새 안내", "안내 문구를 입력해 주세요.", "", ""); gMsgs.CurrentCell = gMsgs.Rows[i].Cells[0]; };
            bMu.Click += (o, e) => MoveGrid(gMsgs, -1); bMd.Click += (o, e) => MoveGrid(gMsgs, 1);
            bR.Click += (o, e) => { if (!AskReset("프로그램에 처음 들어 있던 안내 버튼들로 되돌려요.")) return; gMsgs.Rows.Clear(); foreach (var m in Settings.DefaultMessages()) AddMsgRow(m.Label, m.Text, "", ""); };
            bar.Controls.AddRange(new Control[] { bA, bMu, bMd, bR });
            var note = Note("위젯의 [선생님 안내]를 누르면 나오는 버튼이에요. 이미지를 넣으면 안내할 때 크게 보여 줘요(PNG·JPG·움직이는 GIF). 소리를 넣지 않으면 안내 문구를 읽어 줘요.");
            Page("수동 음성 안내", new Control[] { gMsgs, bar, note }, new float[] { 100, 0, 0 });
        }

        int AddMsgRow(string label, string text, string sound, string image)
        {
            int i = gMsgs.Rows.Add(label, text, SoundText(sound), ImageText(image));
            gMsgs.Rows[i].Tag = new MsgTag { Sound = sound ?? "", Image = image ?? "" };
            return i;
        }

        void CommitVoice()
        {
            gRules.EndEdit(); gMsgs.EndEdit();
            R.Voice = chkVoice.Checked; R.Sfx = chkSfx.Checked;
            if (cbVoice != null) { R.VoiceName = cbVoice.SelectedIndex <= 0 ? "" : cbVoice.SelectedItem as string; R.VoiceRate = slRate.Value; }
            for (int i = 0; i < Settings.RuleKeys.Length && i < gRules.Rows.Count; i++)
            {
                var row = gRules.Rows[i];
                R.Rules[Settings.RuleKeys[i]] = new Rule(row.Cells[0].Value is bool && (bool)row.Cells[0].Value, Times(row.Cells[2].Value), (row.Cells[3].Value as string ?? "").Trim()) { Sound = row.Tag as string };
            }
            R.Messages = new List<Msg>();
            foreach (DataGridViewRow row in gMsgs.Rows)
            {
                string l = (row.Cells[0].Value as string ?? "").Trim(), t = (row.Cells[1].Value as string ?? "").Trim();
                var tag = row.Tag as MsgTag ?? new MsgTag();
                if (l.Length > 0) R.Messages.Add(new Msg(l, t) { Sound = tag.Sound, Image = tag.Image });
            }
        }

        // ---------- 뽑기 ----------
        void BuildPick()
        {
            var top = Flow();
            top.Controls.Add(new Label { Text = "학생 수", AutoSize = true, Margin = new Padding(3, 8, 6, 3) });
            nCount = new NumericUpDown { Minimum = 1, Maximum = 60, Value = Math.Max(1, Math.Min(60, R.StudentCount)), Width = 64, Margin = new Padding(3, 5, 3, 3) };
            nCount.ValueChanged += (o, e) => ResizeStudents((int)nCount.Value);
            top.Controls.Add(nCount);
            top.Controls.Add(new Label { Text = "명", AutoSize = true, Margin = new Padding(3, 8, 24, 3) });
            rbBoth = new Chip { Text = "번호+이름", AutoSize = true, Checked = R.PickMode == 0, Margin = new Padding(3, 6, 6, 3) };
            rbName = new Chip { Text = "이름만", AutoSize = true, Checked = R.PickMode == 1, Margin = new Padding(3, 6, 6, 3) };
            rbNo = new Chip { Text = "번호만", AutoSize = true, Checked = R.PickMode == 2, Margin = new Padding(3, 6, 24, 3) };
            top.Controls.AddRange(new Control[] { rbBoth, rbName, rbNo });
            chkNoRepeat = new Toggle { Text = "한 번 뽑힌 학생은 빼기", AutoSize = true, Checked = R.PickNoRepeat, Margin = new Padding(3, 6, 10, 3) };
            top.Controls.Add(chkNoRepeat);
            chkResetStart = new Toggle { Text = "프로그램을 켤 때마다 뽑힌 기록 지우기", AutoSize = true, Checked = R.PickResetOnStart, Margin = new Padding(3, 6, 10, 3) };
            top.Controls.Add(chkResetStart);

            gStu = NewGrid();
            gStu.Columns.Add(TextCol("번호", 10, true));
            gStu.Columns.Add(TextCol("이름", 34));
            gStu.Columns.Add(ComboCol("성별", 12, "—", "남", "여"));
            gStu.Columns.Add(TextCol("프로필 사진", 26, true));
            gStu.Columns.Add(BtnCol("사진…", 10));
            gStu.Columns.Add(BtnCol("사진 빼기", 10));
            ResizeStudents(R.StudentCount);
            gStu.CellContentClick += (o, e) =>
            {
                if (e.RowIndex < 0) return;
                var row = gStu.Rows[e.RowIndex];
                if (e.ColumnIndex == 4)
                {
                    using (var od = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.gif;*.bmp", Title = (e.RowIndex + 1) + "번 프로필 사진" })
                        if (od.ShowDialog(this) == DialogResult.OK) { var n = Media.Import(od.FileName); if (n != null) { row.Tag = n; row.Cells[3].Value = ImageText(n); } }
                }
                else if (e.ColumnIndex == 5) { row.Tag = ""; row.Cells[3].Value = ImageText(""); }
            };

            var bar = Flow();
            var bXl = B("엑셀로 명단 넣기…"); bXl.Tag = "primary";
            bXl.Click += (o, e) => ImportRoster();
            var bTpl = B("엑셀 서식 받기…");
            bTpl.Click += (o, e) => SaveRosterTemplate();
            var bPhotos = B("사진 한꺼번에 넣기…");
            bPhotos.Click += (o, e) => ImportPhotos();
            var bPaste = B("붙여 넣기…");
            bPaste.Click += (o, e) => PasteNames();
            var bClear = B("이름 모두 지우기");
            bClear.Click += (o, e) => { foreach (DataGridViewRow r in gStu.Rows) r.Cells[1].Value = ""; };
            lPicked = new Label { AutoSize = true, Margin = new Padding(16, 9, 6, 3), ForeColor = Color.DimGray };
            var bReset = B("뽑힌 기록 지우기");
            bReset.Click += (o, e) => { R.Picked.Clear(); UpdatePicked(); };
            bar.Controls.AddRange(new Control[] { bTpl, bXl, bPhotos, bPaste, bClear, lPicked, bReset });
            UpdatePicked();
            var note = Note("[엑셀로 명단 넣기]는 나이스 명렬표처럼 번호·성명·성별 열이 있는 파일을 그대로 읽어요. 엑셀 칸에 사진을 넣어 두면 사진도 함께 들어가요. 사진 파일만 따로 있으면 [사진 한꺼번에 넣기]로 여러 장을 고르세요(파일 이름에 이름이나 번호). 이름을 비워 두면 번호로 뽑아요. 성별을 정해 두면 뽑기 창에서 [남자만]·[여자만]으로 뽑을 수 있어요. 프로필 사진을 넣으면 뽑힐 때 사진도 함께 나와요. 위젯의 [뽑기]를 누르면 시작해요.");
            Page("뽑기 도구", new Control[] { top, gStu, bar, note }, new float[] { 0, 100, 0, 0 });
        }

        static string GenderText(string g) { return g == "남" || g == "여" ? g : "—"; }
        static string ParseGender(string t)
        {
            t = t.Trim().Trim('(', ')', '[', ']');
            if (t == "남" || t == "남자" || t == "M" || t == "m") return "남";
            if (t == "여" || t == "여자" || t == "F" || t == "f") return "여";
            return null;
        }

        void UpdatePicked() { lPicked.Text = "지금까지 뽑힌 학생 " + R.Picked.Count + "명"; }

        void ResizeStudents(int n)
        {
            gStu.EndEdit();
            while (gStu.Rows.Count > n) gStu.Rows.RemoveAt(gStu.Rows.Count - 1);
            while (gStu.Rows.Count < n)
            {
                int no = gStu.Rows.Count + 1;
                var st = R.StudentOf(no);
                int i = gStu.Rows.Add(no + "번", st != null ? st.Name : "", GenderText(st != null ? st.Gender : ""), ImageText(st != null ? st.Photo : ""));
                gStu.Rows[i].Tag = st != null ? st.Photo ?? "" : "";
            }
        }

        void SaveRosterTemplate()
        {
            gStu.EndEdit();
            using (var sd = new SaveFileDialog { Filter = "엑셀 파일|*.xlsx", FileName = "RamClock 명렬표 서식.xlsx", Title = "명렬표 서식 저장" })
            {
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                var rows = new List<string[]>();
                for (int i = 0; i < gStu.Rows.Count; i++)
                {
                    var r = gStu.Rows[i];
                    string g = r.Cells[2].Value as string; if (g != "남" && g != "여") g = "";
                    rows.Add(new[] { (i + 1).ToString(), (r.Cells[1].Value as string ?? "").Trim(), g });
                }
                try
                {
                    RosterImport.WriteTemplate(sd.FileName, rows);
                    try { System.Diagnostics.Process.Start(sd.FileName); } catch { }
                }
                catch (Exception ex) { MessageBox.Show(this, "저장하지 못했어요.\n" + ex.Message, "RamClock"); }
            }
        }

        void ImportRoster()
        {
            string path;
            using (var od = new OpenFileDialog { Filter = "명렬표 (엑셀·CSV)|*.xlsx;*.xlsm;*.xls;*.csv;*.cell|모든 파일|*.*", Title = "명렬표 엑셀 파일 고르기" })
            {
                if (od.ShowDialog(this) != DialogResult.OK) return;
                path = od.FileName;
            }
            string msg; List<RosterRow> rows;
            Cursor = Cursors.WaitCursor;
            try { rows = RosterImport.Read(path, out msg); } finally { Cursor = Cursors.Default; }
            if (rows == null || rows.Count == 0) { MessageBox.Show(this, msg ?? "명단을 찾지 못했어요.", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            int max = 0; foreach (var r in rows) max = Math.Max(max, r.No);
            nCount.Value = Math.Max(1, Math.Min(60, max));
            int photos = 0, gens = 0;
            foreach (var r in rows)
            {
                if (r.No < 1 || r.No > gStu.Rows.Count) continue;
                var row = gStu.Rows[r.No - 1];
                row.Cells[1].Value = r.Name;
                if (r.Gender != "") { row.Cells[2].Value = r.Gender; gens++; }
                if (r.Photo != null)
                {
                    try
                    {
                        string tmp = Path.Combine(Path.GetTempPath(), r.No.ToString("00") + "_" + Safe(r.Name) + (string.IsNullOrEmpty(r.PhotoExt) ? ".png" : r.PhotoExt));
                        File.WriteAllBytes(tmp, r.Photo);
                        var n = Media.Import(tmp);
                        try { File.Delete(tmp); } catch { }
                        if (n != null) { row.Tag = n; row.Cells[3].Value = ImageText(n); photos++; }
                    }
                    catch { }
                }
            }
            MessageBox.Show(this, rows.Count + "명을 넣었어요." + (gens > 0 ? "\n성별 " + gens + "명" : "") + (photos > 0 ? "\n사진 " + photos + "장" : "") + "\n\n[저장]을 누르면 적용돼요.", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string Safe(string s) { foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }

        void ImportPhotos()
        {
            string[] files;
            using (var od = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp", Multiselect = true, Title = "학생 사진 여러 장 고르기 (Ctrl+A로 모두 선택)" })
            {
                if (od.ShowDialog(this) != DialogResult.OK) return;
                files = od.FileNames;
            }
            gStu.EndEdit();
            var names = new List<string>();
            foreach (DataGridViewRow r in gStu.Rows) names.Add((r.Cells[1].Value as string ?? "").Trim());
            int ok = 0; var miss = new List<string>();
            foreach (var f in files)
            {
                int no = RosterImport.MatchPhoto(f, names);
                if (no < 1 || no > gStu.Rows.Count) { miss.Add(Path.GetFileName(f)); continue; }
                var n = Media.Import(f);
                if (n == null) continue;
                gStu.Rows[no - 1].Tag = n; gStu.Rows[no - 1].Cells[3].Value = ImageText(n); ok++;
            }
            string m = ok + "장을 학생에게 넣었어요.";
            if (miss.Count > 0) m += "\n\n누구 사진인지 모르는 파일 " + miss.Count + "개:\n" + string.Join(", ", miss.GetRange(0, Math.Min(8, miss.Count)).ToArray()) + (miss.Count > 8 ? " …" : "") + "\n파일 이름에 학생 이름이나 번호(예: 03_김하늘.jpg)가 들어 있으면 자동으로 찾아요.";
            MessageBox.Show(this, m + "\n\n[저장]을 누르면 적용돼요.", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void PasteNames()
        {
            using (var f = new Form { Text = "명단 한꺼번에 넣기", ClientSize = new Size(420, 460), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = Font, AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96F, 96F) })
            {
                var t = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1 };
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                t.Controls.Add(new Label { Text = "한 줄에 한 명씩 붙여 넣어 주세요. 엑셀에서 이름 열을 복사해 붙여도 돼요.\n\"3 김예람 여\"처럼 번호·성별을 함께 써도 돼요. (엑셀에서 번호·이름·성별 열을 같이 복사해도 OK)", AutoSize = true, MaximumSize = new Size(390, 0) });
                var tb = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
                t.Controls.Add(tb);
                var ok = new GButton { Text = "넣기", AutoSize = true, MinimumSize = new Size(90, 32), DialogResult = DialogResult.OK };
                t.Controls.Add(ok);
                f.Controls.Add(t); f.AcceptButton = null;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var lines = new List<string>();
                foreach (var ln in tb.Text.Replace("\r", "").Split('\n')) if (ln.Trim().Length > 0) lines.Add(ln.Trim());
                if (lines.Count == 0) return;
                // "3  김예람  여", "김예람 남", 엑셀 [번호][이름][성별] 복사 모두 가능
                var nos = new List<int>(); var names = new List<string>(); var gens = new List<string>();
                int auto = 1;
                foreach (var ln in lines)
                {
                    var toks = new List<string>(ln.Split(new[] { '\t', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
                    int no = -1; string gen = null;
                    if (toks.Count > 0)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(toks[0], @"^(\d{1,2})(번|\.)?$");
                        if (m.Success) { no = int.Parse(m.Groups[1].Value); toks.RemoveAt(0); }
                    }
                    for (int k = toks.Count - 1; k >= 0; k--)
                    {
                        string g2 = ParseGender(toks[k]);
                        if (g2 != null) { if (gen == null) gen = g2; toks.RemoveAt(k); }
                    }
                    if (no < 0) no = auto;
                    nos.Add(no); names.Add(string.Join(" ", toks)); gens.Add(gen);
                    auto = no + 1;
                }
                int max = 0; foreach (var v in nos) max = Math.Max(max, v);
                if (max > nCount.Value) nCount.Value = Math.Min(60, max);
                for (int k = 0; k < nos.Count; k++)
                {
                    if (nos[k] < 1 || nos[k] > gStu.Rows.Count) continue;
                    var row = gStu.Rows[nos[k] - 1];
                    if (names[k].Length > 0) row.Cells[1].Value = names[k];
                    if (gens[k] != null) row.Cells[2].Value = gens[k];
                }
            }
        }

        void CommitPick()
        {
            gStu.EndEdit();
            R.StudentCount = (int)nCount.Value;
            R.PickMode = rbName.Checked ? 1 : rbNo.Checked ? 2 : 0;
            R.PickNoRepeat = chkNoRepeat.Checked;
            R.PickResetOnStart = chkResetStart.Checked;
            R.Students = new List<Student>();
            for (int i = 0; i < gStu.Rows.Count; i++)
            {
                var row = gStu.Rows[i];
                string name = (row.Cells[1].Value as string ?? "").Trim(), photo = row.Tag as string ?? "";
                string gen = row.Cells[2].Value as string; if (gen != "남" && gen != "여") gen = "";
                if (name.Length > 0 || photo.Length > 0 || gen.Length > 0) R.Students.Add(new Student(i + 1, name) { Photo = photo, Gender = gen });
            }
            R.Picked.RemoveAll(n => n > R.StudentCount);
        }

        // ---------- 바로가기 ----------
        DataGridView gLinks;
        void BuildLinks()
        {
            gLinks = NewGrid();
            gLinks.Columns.Add(TextCol("버튼 이름", 25));
            gLinks.Columns.Add(TextCol("주소 (사이트 주소나 파일·폴더 위치)", 60));
            gLinks.Columns.Add(BtnCol("열어 보기", 12));
            AddDeleteCol(gLinks, null);
            foreach (var l in R.Links) gLinks.Rows.Add(l.Name, l.Url);
            gLinks.CellContentClick += (o, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 2) return;
                gLinks.EndEdit();
                string u = (gLinks.Rows[e.RowIndex].Cells[1].Value as string ?? "").Trim();
                if (u.Length == 0) return;
                if (!u.Contains("://") && !File.Exists(u) && !Directory.Exists(u)) u = "https://" + u;
                try { System.Diagnostics.Process.Start(u); } catch { MessageBox.Show(this, "열지 못했어요. 주소를 확인해 주세요.", "RamClock"); }
            };
            var bar = Flow();
            var bA = B("+ 사이트 추가…"); bA.Tag = "primary"; var bUp = Arrow("▲", "고른 줄을 앞으로"); var bDn = Arrow("▼", "고른 줄을 뒤로"); var bR = B("처음 상태로 되돌리기");
            bA.Click += (o, e) => { using (var d = new LinkDialog(null)) if (d.ShowDialog(this) == DialogResult.OK) AddLinkRow(d.Result); };
            bUp.Click += (o, e) => MoveGrid(gLinks, -1); bDn.Click += (o, e) => MoveGrid(gLinks, 1);
            bR.Click += (o, e) => { if (!AskReset("처음 들어 있던 바로가기(아이스크림·유튜브)로 되돌려요.")) return; gLinks.Rows.Clear(); foreach (var l in Settings.DefaultLinks()) gLinks.Rows.Add(l.Name, l.Url); };
            bar.Controls.AddRange(new Control[] { bA, bUp, bDn, bR });
            var note = Note("시계에 마우스를 올리면 시계 위쪽에 로고 버튼으로 나타나요. 누르면 기본 브라우저(또는 그 파일·프로그램)로 열려요. 이름·주소 칸은 눌러서 바로 고칠 수 있어요.");

            // 교실 도구 버튼 고르기
            var tools = Flow();
            tools.Controls.Add(new Label { Text = "시계 아래 [교실 도구]에 보일 버튼:", AutoSize = true, Margin = new Padding(3, 9, 12, 3) });
            toolChecks = new List<CheckBox>();
            foreach (var t in Settings.ToolList)
            {
                var tg = new Toggle { Text = t[1], Tag = t[0], AutoSize = true, Checked = R.Tools.Contains(t[0]), Margin = new Padding(0, 6, 16, 3) };
                toolChecks.Add(tg); tools.Controls.Add(tg);
            }
            var lLinks = new Label { Text = "자주 가는 사이트", AutoSize = true, Font = Media.UiFont(10.5F, true), Margin = new Padding(3, 14, 3, 4) };
            Page("바로가기 도구", new Control[] { tools, lLinks, gLinks, bar, note }, new float[] { 0, 0, 100, 0, 0 });
        }

        List<CheckBox> toolChecks;
        Action showWeek;
        public void ShowWeekSubTab() { if (showWeek != null) showWeek(); }
        public void AddLinkRow(QuickLink l) { if (gLinks == null || l == null) return; int i = gLinks.Rows.Add(l.Name, l.Url); gLinks.CurrentCell = gLinks.Rows[i].Cells[0]; }

        void CommitLinks()
        {
            gLinks.EndEdit();
            if (toolChecks != null) { R.Tools = new List<string>(); foreach (var c in toolChecks) if (c.Checked) R.Tools.Add((string)c.Tag); }
            R.Links = new List<QuickLink>();
            foreach (DataGridViewRow row in gLinks.Rows)
            {
                string n = (row.Cells[0].Value as string ?? "").Trim(), u = (row.Cells[1].Value as string ?? "").Trim();
                if (u.Length == 0 || u == "https://") continue;
                R.Links.Add(new QuickLink(n.Length > 0 ? n : u, u));
            }
        }

        // ---------- 도움말 ----------
        void BuildHelp()
        {
            var tb = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window,
                Text = string.Join("\r\n", new[] {
                    "■ 기본 사용",
                    "· 글씨와 숫자만 보이고 배경은 투명해요. 다른 창 위에 항상 떠 있어요.",
                    "· 위젯에 마우스를 올리면 아래에 버튼이 나타나요.",
                    "· 위젯을 끌어서 옮기고, 네 모서리의 동그라미를 끌어서 크기를 바꿔요. (마우스 휠도 돼요)",
                    "· 오른쪽 클릭하면 메뉴(숨기기·설정·종료)가 나와요.",
                    "",
                    "■ 아래 버튼",
                    "· 모드: 시간표 / 타이머 / 스톱워치",
                    "· 교실 도구: 선생님 안내, 뽑기, 이번 주(주간 시간표)",
                    "· 설정: 색·글꼴·효과 같은 화면 설정과 시정표, 명단 등을 정해요.",
                    "",
                    "■ 주간학습안내",
                    "· [이번 주] → [주간학습안내 PDF 불러오기]로 그 주 PDF를 넣으면 요일·교시별 과목과 내용을 읽어 와요.",
                    "· 읽은 결과는 표로 확인하고, 틀린 칸은 두 번 눌러 고쳐요. 그 주에는 그 과목으로 안내해요.",
                    "· 한글에서 PDF로 저장한 파일은 읽을 수 있지만, 스캔한 그림 PDF는 읽을 수 없어요.",
                    "",
                    "■ 소리",
                    "· 안내마다 소리 파일(mp3·wav)을 넣거나 마이크로 직접 녹음할 수 있어요. 없으면 안내 문구를 읽어 줘요.",
                    "",
                    "■ 숨기기와 종료",
                    "· [숨기기]를 누르면 작업 표시줄 오른쪽 아래(트레이)로 들어가요. 아이콘을 누르면 다시 나와요.",
                    "· 완전히 끄려면 트레이 아이콘이나 위젯을 오른쪽 클릭해서 [종료]를 눌러요.",
                    "",
                    "RamClock v" + Updater.Version + " · 예람쌤이 만든 교실 시계 (새 버전은 자동으로 받아져요)",
                    "",
                    "■ 들어 있는 글꼴 (★ 표시) — 모두 SIL 오픈 폰트 라이선스(OFL)로 무료 배포되는 글꼴이에요.",
                    "  나눔고딕·나눔손글씨 펜 (네이버), 주아·도현 (우아한형제들), 검은고딕 (Zess Type), 해바라기 (JIKJI SOFT), 개구 (JIKJI SOFT)"
                })
            };
            Page("도움말", new Control[] { tb }, new float[] { 100 });
        }

        // ---------- 저장 ----------
        void SaveAndClose()
        {
            if (!CommitSched(false))
            {
                nav.SelectedIndex = titles.IndexOf("시정표");
                MessageBox.Show(this, "시정표의 빨간 칸 시간을 확인해 주세요.\n09:00처럼 적고, 끝 시각은 시작보다 늦어야 해요.", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            CommitTt(); CommitVoice(); CommitScreen(); CommitPick(); CommitLinks();
            if (Saved != null) Saved(R);
            lSaved.Text = "✓ 저장했어요. 위젯에 적용됐어요.";
            lSaved.ForeColor = Color.FromArgb(30, 150, 90);
            savedTimer.Stop(); savedTimer.Start();
        }
    }
}
