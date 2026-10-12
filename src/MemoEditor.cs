using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 시계 바로 아래에서 메모를 곧바로 쓰는 작은 입력칸 — 메모장처럼 위쪽 줄에서 글꼴·크기·포스트잇 색을 바로 바꾼다
    public class MemoEditor : Form
    {
        public Action<string> Changed;
        public Action StyleRequested;
        public string MemoText { get { return tb.Text.Replace("\r", "").TrimEnd('\n', ' '); } }

        readonly Settings S;
        readonly TextBox tb;
        readonly Label hint;
        readonly ComboBox cbFont;
        readonly List<Control> bar = new List<Control>();
        readonly List<Dot> dots = new List<Dot>();
        readonly Label bMinus, bPlus, bBold, bMore, bClear, bDone;
        readonly float s;
        readonly Font small;
        Color bg, fg;
        bool closing, holdOpen;

        // 포스트잇 색 (마지막은 '바탕 없음')
        static readonly string[] Notes = { "#FFF3A3", "#FFD3E0", "#CDF5DD", "#D3ECFF", "#E6DCFF", "#FFE2C2", "#FFFFFF", "" };

        public MemoEditor(Settings settings, float scale)
        {
            S = settings; s = scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Opacity = 0.97;
            DoubleBuffered = true;

            small = new Font(Painter.UiFam, 12.5f * Math.Max(0.85f, s), FontStyle.Regular, GraphicsUnit.Pixel);

            tb = new TextBox
            {
                Multiline = true, AcceptsReturn = true, BorderStyle = BorderStyle.None, WordWrap = true,
                ScrollBars = ScrollBars.None, TextAlign = HorizontalAlignment.Center,
                Text = (S.Memo ?? "").Replace("\r", "").Replace("\n", "\r\n")
            };
            tb.TextChanged += (o, e) => { Fit(); if (Changed != null) Changed(MemoText); hint.Visible = tb.TextLength == 0; };
            Controls.Add(tb);

            hint = new Label { Text = "여기에 바로 쓰세요 (Enter 줄바꿈 · Esc 또는 바깥 누르면 끝)", AutoSize = false, Font = small, TextAlign = ContentAlignment.MiddleCenter, Visible = tb.TextLength == 0 };
            hint.Click += (o, e) => tb.Focus();
            Controls.Add(hint); hint.BringToFront();

            // ---- 위쪽 꾸미기 줄 ----
            cbFont = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = small, Width = (int)(150 * Math.Max(0.85f, s)), MaxDropDownItems = 18 };
            cbFont.Items.Add("기본 글꼴");
            foreach (var n in BuiltinFonts.Names) cbFont.Items.Add(n);
            try { using (var ifc = new System.Drawing.Text.InstalledFontCollection()) foreach (var fam in ifc.Families) if (!fam.Name.StartsWith("@")) cbFont.Items.Add(fam.Name); } catch { }
            if (!string.IsNullOrEmpty(S.MemoFont) && !cbFont.Items.Contains(S.MemoFont)) cbFont.Items.Insert(1, S.MemoFont);
            cbFont.SelectedItem = string.IsNullOrEmpty(S.MemoFont) ? "기본 글꼴" : S.MemoFont;
            if (cbFont.SelectedIndex < 0) cbFont.SelectedIndex = 0;
            cbFont.SelectedIndexChanged += (o, e) => { S.MemoFont = cbFont.SelectedIndex == 0 ? "" : cbFont.SelectedItem as string; ApplyStyle(); tb.Focus(); };
            cbFont.DropDown += (o, e) => holdOpen = true;
            cbFont.DropDownClosed += (o, e) => { holdOpen = false; };
            bar.Add(cbFont);

            bMinus = Tool("가−", "글자 작게"); bMinus.Click += (o, e) => { S.MemoSize = Math.Max(12, S.MemoSize - 2); ApplyStyle(); };
            bPlus = Tool("가+", "글자 크게"); bPlus.Click += (o, e) => { S.MemoSize = Math.Min(48, S.MemoSize + 2); ApplyStyle(); };
            bBold = Tool("굵게", "굵게"); bBold.Click += (o, e) => { S.MemoBold = !S.MemoBold; ApplyStyle(); };
            bar.Add(bMinus); bar.Add(bPlus); bar.Add(bBold);

            foreach (var hex in Notes)
            {
                var d = new Dot { Hex = hex, Width = (int)(20 * Math.Max(0.85f, s)), Height = (int)(20 * Math.Max(0.85f, s)), Cursor = Cursors.Hand };
                new ToolTip().SetToolTip(d, hex == "" ? "바탕 없이 글자만" : "포스트잇 색");
                d.Click += (o, e) =>
                {
                    var h = ((Dot)o).Hex;
                    if (h == "") S.MemoBgOn = false;
                    else { S.MemoBgOn = true; S.MemoBgColor = h; if (S.MemoBgOpacity < 60) S.MemoBgOpacity = 92; if (Lum(Painter.ParseColor(S.MemoColor)) > 0.6f) S.MemoColor = "#2A2E3C"; }
                    ApplyStyle(); tb.Focus();
                };
                dots.Add(d); bar.Add(d);
            }
            bMore = Tool("글자색…", "글자 색 고르기");
            bMore.Click += (o, e) =>
            {
                holdOpen = true;
                Color c;
                if (ColorPicker.Pick(this, Painter.ParseColor(S.MemoColor), out c)) { S.MemoColor = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
                holdOpen = false; ApplyStyle(); Activate(); tb.Focus();
            };
            bar.Add(bMore);
            bClear = Tool("지우기", "글 모두 지우기"); bClear.Click += (o, e) => { tb.Clear(); tb.Focus(); };
            bDone = Tool("완료", "다 썼어요"); bDone.Font = new Font(small, FontStyle.Bold); bDone.Click += (o, e) => Close();
            bar.Add(bClear); bar.Add(bDone);
            foreach (var c in bar) Controls.Add(c);

            Deactivate += (o, e) => { if (!closing && !holdOpen) BeginInvoke(new Action(() => { if (!IsDisposed && !closing && !holdOpen && !ContainsFocus) Close(); })); };
            FormClosing += (o, e) => closing = true;
            Shown += (o, e) => { tb.Focus(); tb.SelectionStart = tb.TextLength; };
            ApplyStyle();
        }

        static float Lum(Color c) { return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f; }

        Label Tool(string t, string tip)
        {
            var l = new Label { Text = t, AutoSize = true, Font = small, Cursor = Cursors.Hand, Padding = new Padding(5, 3, 5, 3), TextAlign = ContentAlignment.MiddleCenter };
            new ToolTip().SetToolTip(l, tip);
            return l;
        }

        // 지금 꾸미기 값을 입력칸 모양에 반영
        void ApplyStyle()
        {
            bg = S.MemoBgOn ? Painter.ParseColor(S.MemoBgColor) : Color.FromArgb(250, 250, 253);
            fg = Painter.ParseColor(S.MemoColor);
            if (Math.Abs(Lum(bg) - Lum(fg)) < 0.35f) fg = Lum(bg) > 0.5f ? Color.FromArgb(34, 38, 52) : Color.White;
            BackColor = bg; tb.BackColor = bg; tb.ForeColor = fg; hint.BackColor = bg;
            Color muted = Lum(bg) > 0.5f ? Color.FromArgb(110, 116, 135) : Color.FromArgb(205, 210, 225);
            hint.ForeColor = muted;
            foreach (var c in bar)
            {
                var l = c as Label; if (l == null) continue;
                l.BackColor = bg; l.ForeColor = l == bDone ? Ui.Accent : muted;
            }
            bBold.ForeColor = S.MemoBold ? Ui.Accent : muted;
            bBold.Font = new Font(small, S.MemoBold ? FontStyle.Bold : FontStyle.Regular);
            foreach (var d in dots) { d.BackColor = bg; d.Selected = d.Hex == "" ? !S.MemoBgOn : (S.MemoBgOn && string.Equals(d.Hex, S.MemoBgColor, StringComparison.OrdinalIgnoreCase)); d.Invalidate(); }
            try
            {
                var ff = string.IsNullOrEmpty(S.MemoFont) ? Painter.FamFor(S) : Painter.HudFam(S.MemoFont);
                var fs = S.MemoBold && ff.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : (ff.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold);
                tb.Font = new Font(ff, Math.Max(10, S.MemoSize) * s, fs, GraphicsUnit.Pixel);
            }
            catch { tb.Font = new Font(Painter.UiFam, Math.Max(10, S.MemoSize) * s, FontStyle.Regular, GraphicsUnit.Pixel); }
            Fit();
            Invalidate(true);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape || (e.KeyCode == Keys.Enter && e.Control)) { e.Handled = true; e.SuppressKeyPress = true; Close(); }
        }

        int centerX, top, minW;
        public void Place(Rectangle content, int y)
        {
            minW = Math.Max(content.Width, (int)(470 * Math.Max(0.85f, s)));
            centerX = content.Left + content.Width / 2;
            top = y;
            Fit();
        }

        void Fit()
        {
            if (minW == 0 || tb == null) return;
            int pad = (int)(12 * s), gap = (int)(4 * Math.Max(0.85f, s));
            // 위쪽 꾸미기 줄 (넘치면 두 줄)
            int w = minW, x = pad, y = (int)(8 * s), rowH = 0;
            foreach (var c in bar)
            {
                int cw = c is Label ? ((Label)c).PreferredWidth : c.Width, chh = c is Label ? ((Label)c).PreferredHeight : c.Height;
                if (x + cw > w - pad && x > pad) { x = pad; y += rowH + gap; rowH = 0; }
                rowH = Math.Max(rowH, Math.Max(chh, cbFont.Height));
                c.SetBounds(x, 0, cw, chh);
                c.Tag = y;
                x += cw + gap + (c == bBold || c == dots[dots.Count - 1] || c == bMore ? gap * 2 : 0);
            }
            foreach (var c in bar) { int cy = (int)c.Tag; int chh = c.Height; c.Top = cy + (Math.Max(rowH, cbFont.Height) - chh) / 2; }
            int barBottom = y + rowH + (int)(6 * s);

            int lineH = TextRenderer.MeasureText("가", tb.Font).Height;
            int textW = w - pad * 2;
            int lines = 0;
            foreach (var ln in tb.Text.Replace("\r", "").Split('\n'))
            {
                int lw = TextRenderer.MeasureText(ln.Length == 0 ? " " : ln, tb.Font).Width;
                lines += Math.Max(1, (int)Math.Ceiling(lw / (double)Math.Max(1, textW - 8)));
            }
            lines = Math.Max(1, Math.Min(12, lines));
            int th = lines * lineH + 4;
            int h = barBottom + (int)(4 * s) + th + pad;
            SetBounds(centerX - w / 2, top, w, h);
            tb.SetBounds(pad, barBottom + (int)(4 * s), textW, th);
            hint.SetBounds(pad, barBottom + (int)(4 * s), textW, th);
            lineY = barBottom;
            using (var p = Painter.RoundPath(new RectangleF(0, 0, w, h), 14 * s)) Region = new Region(p);
            Invalidate();
        }
        int lineY;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f)) g.DrawLine(pen, 12 * s, lineY, Width - 12 * s, lineY);
            using (var p = Painter.RoundPath(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 14 * s))
            using (var pen = new Pen(Color.FromArgb(150, Ui.Accent), Math.Max(1.5f, 2 * s))) g.DrawPath(pen, p);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }   // 작업 표시줄·Alt+Tab에 안 보이게
        }

        // 포스트잇 색 동그라미
        class Dot : Control
        {
            public string Hex; public bool Selected;
            public Dot() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BackColor);
                var r = new RectangleF(2, 2, Width - 5, Height - 5);
                if (Hex == "")
                {
                    using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, r);
                    using (var pen = new Pen(Color.FromArgb(230, 90, 90), 1.6f)) g.DrawLine(pen, r.Left + 3, r.Bottom - 3, r.Right - 3, r.Top + 3);
                }
                else using (var b = new SolidBrush(Painter.ParseColor(Hex))) g.FillEllipse(b, r);
                using (var pen = new Pen(Selected ? Ui.Accent : Color.FromArgb(70, 0, 0, 0), Selected ? 2.4f : 1f)) g.DrawEllipse(pen, r);
            }
        }
    }
}
