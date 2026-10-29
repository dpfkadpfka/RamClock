using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 시계 바로 아래에서 메모를 곧바로 쓰는 작은 입력칸
    // 글자를 골라서(드래그) 굵게·기울임·밑줄·취소선·색·크기를 부분만 바꿀 수 있고, 아무것도 안 고르면 전체가 바뀐다
    public class MemoEditor : Form
    {
        public Action<string> Changed;
        public Action StyleRequested;
        public string MemoText { get { return rtb.Text.Replace("\r", "").TrimEnd('\n', ' '); } }

        readonly Settings S;
        readonly RichTextBox rtb;
        readonly Label hint;
        readonly ComboBox cbFont;
        readonly List<Control> bar = new List<Control>();
        readonly List<Dot> dots = new List<Dot>();
        readonly Label bMinus, bPlus, bBold, bItalic, bUnder, bStrike, bClear, bDone;
        readonly ColorA bColor;
        readonly float s;
        readonly Font small;
        Color bg, fg;
        bool closing, holdOpen, loading;
        int contentH;

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
            DoubleBuffered = true;

            small = new Font(Painter.UiFam, 12.5f * Math.Max(0.85f, s), FontStyle.Regular, GraphicsUnit.Pixel);

            rtb = new RichTextBox
            {
                Multiline = true, AcceptsTab = false, BorderStyle = BorderStyle.None, WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.None, DetectUrls = false, ShortcutsEnabled = true
            };
            rtb.ContentsResized += (o, e) => { int h = e.NewRectangle.Height; if (h != contentH) { contentH = h; Fit(); } };
            rtb.TextChanged += (o, e) =>
            {
                if (loading) return;
                bool hv = rtb.TextLength == 0; if (hint.Visible != hv) { hint.Visible = hv; Fit(); }
                if (rtb.TextLength > 0 && rtb.SelectionAlignment != HorizontalAlignment.Center) CenterAll();
                if (Changed != null) Changed(MemoText);
            };
            rtb.SelectionChanged += (o, e) => { if (!loading) ShowState(); BoldCaret(); };
            rtb.GotFocus += (o, e) => BoldCaret();
            rtb.KeyUp += (o, e) => BoldCaret();
            rtb.MouseUp += (o, e) => BoldCaret();
            rtb.ContentsResized += (o, e) => BoldCaret();
            Controls.Add(rtb);

            // 안내 글은 입력칸 아래 한 줄에 (입력칸을 덮으면 커서가 가려짐)
            hint = new Label { Text = "여기에 바로 쓰세요 · 글자를 드래그해 고르면 그 부분만 꾸며져요", AutoSize = false, Font = small, TextAlign = ContentAlignment.MiddleCenter };
            hint.Click += (o, e) => rtb.Focus();
            Controls.Add(hint);

            // ---- 위쪽 꾸미기 줄 ----
            cbFont = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = small, Width = (int)(140 * Math.Max(0.85f, s)), MaxDropDownItems = 18 };
            cbFont.Items.Add("기본 글꼴");
            foreach (var n in BuiltinFonts.Names) cbFont.Items.Add(n);
            try { using (var ifc = new System.Drawing.Text.InstalledFontCollection()) foreach (var fam in ifc.Families) if (!fam.Name.StartsWith("@")) cbFont.Items.Add(fam.Name); } catch { }
            if (!string.IsNullOrEmpty(S.MemoFont) && !cbFont.Items.Contains(S.MemoFont)) cbFont.Items.Insert(1, S.MemoFont);
            cbFont.SelectedItem = string.IsNullOrEmpty(S.MemoFont) ? "기본 글꼴" : S.MemoFont;
            if (cbFont.SelectedIndex < 0) cbFont.SelectedIndex = 0;
            cbFont.SelectedIndexChanged += (o, e) => { if (loading) return; var runs = Runs; S.MemoFont = cbFont.SelectedIndex == 0 ? "" : cbFont.SelectedItem as string; Load(runs, true); ApplyLook(); rtb.Focus(); };
            cbFont.DropDown += (o, e) => holdOpen = true;
            cbFont.DropDownClosed += (o, e) => { holdOpen = false; };
            new ToolTip().SetToolTip(cbFont, "글꼴 (전체)");
            bar.Add(cbFont);

            bMinus = Tool("가−", "글자 작게 (고른 부분만, 안 고르면 전체)", FontStyle.Regular); bMinus.Click += (o, e) => ResizeSel(-2);
            bPlus = Tool("가+", "글자 크게 (고른 부분만, 안 고르면 전체)", FontStyle.Regular); bPlus.Click += (o, e) => ResizeSel(2);
            bBold = Tool("B", "굵게 (Ctrl+B)", FontStyle.Bold); bBold.Click += (o, e) => Toggle(FontStyle.Bold);
            bItalic = Tool("I", "기울임 (Ctrl+I)", FontStyle.Italic); bItalic.Click += (o, e) => Toggle(FontStyle.Italic);
            bUnder = Tool("U", "밑줄 (Ctrl+U)", FontStyle.Underline); bUnder.Click += (o, e) => Toggle(FontStyle.Underline);
            bStrike = Tool("S", "취소선", FontStyle.Strikeout); bStrike.Click += (o, e) => Toggle(FontStyle.Strikeout);
            bColor = new ColorA { Width = (int)(26 * Math.Max(0.85f, s)), Height = (int)(26 * Math.Max(0.85f, s)), Cursor = Cursors.Hand, Font = new Font(small.FontFamily, small.Size * 1.1f, FontStyle.Bold, GraphicsUnit.Pixel) };
            new ToolTip().SetToolTip(bColor, "글자 색 (고른 부분만, 안 고르면 전체)");
            bColor.Click += (o, e) => PickColor();
            bar.Add(bMinus); bar.Add(bPlus); bar.Add(bBold); bar.Add(bItalic); bar.Add(bUnder); bar.Add(bStrike); bar.Add(bColor);

            foreach (var hex in Notes)
            {
                var d = new Dot { Hex = hex, Width = (int)(20 * Math.Max(0.85f, s)), Height = (int)(20 * Math.Max(0.85f, s)), Cursor = Cursors.Hand };
                new ToolTip().SetToolTip(d, hex == "" ? "바탕 없이 글자만" : "포스트잇 색");
                d.Click += (o, e) =>
                {
                    var h = ((Dot)o).Hex;
                    if (h == "") S.MemoBgOn = false;
                    else { S.MemoBgOn = true; S.MemoBgColor = h; if (S.MemoBgOpacity < 60) S.MemoBgOpacity = 92; if (Lum(Painter.ParseColor(S.MemoColor)) > 0.6f) S.MemoColor = "#2A2E3C"; }
                    var runs = Runs; ApplyLook(); Load(runs, true); rtb.Focus();
                };
                dots.Add(d); bar.Add(d);
            }
            bClear = Tool("지우기", "글 모두 지우기", FontStyle.Regular); bClear.Click += (o, e) => { rtb.Clear(); S.MemoRuns = null; rtb.Focus(); };
            bDone = Tool("완료", "다 썼어요 (Esc)", FontStyle.Bold); bDone.Click += (o, e) => Close();
            bar.Add(bClear); bar.Add(bDone);
            foreach (var c in bar) Controls.Add(c);

            Deactivate += (o, e) => { if (!closing && !holdOpen) BeginInvoke(new Action(() => { if (!IsDisposed && !closing && !holdOpen && !ContainsFocus) Close(); })); };
            FormClosing += (o, e) => { closing = true; try { S.Memo = MemoText; S.MemoRuns = Runs; } catch { } };
            Shown += (o, e) => { rtb.Focus(); rtb.Select(rtb.TextLength, 0); };

            ApplyLook();
            Load(Painter.RunsOf(S), false);
            hint.Visible = rtb.TextLength == 0;
        }

        // 윈도우 기본 커서(1px)는 큰 글자 위에서 잘 안 보여서 굵게 다시 그림
        [DllImport("user32.dll")] static extern bool CreateCaret(IntPtr h, IntPtr bmp, int w, int ht);
        [DllImport("user32.dll")] static extern bool ShowCaret(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetCaretPos(out Point p);
        [DllImport("user32.dll")] static extern bool SetCaretPos(int x, int y);
        bool caretQueued;
        void BoldCaret()
        {
            if (caretQueued || !rtb.IsHandleCreated) return;
            caretQueued = true;
            BeginInvoke(new Action(() =>
            {
                caretQueued = false;
                try
                {
                    if (IsDisposed || !rtb.Focused || rtb.SelectionLength > 0) return;
                    Point p; GetCaretPos(out p);
                    var f = rtb.SelectionFont;
                    int ht = (int)Math.Ceiling(f != null ? f.GetHeight() : BaseEm * 1.25f);
                    int w = Math.Max(3, (int)Math.Round(3 * s));
                    CreateCaret(rtb.Handle, IntPtr.Zero, w, ht);
                    SetCaretPos(Math.Max(0, p.X - w / 2), p.Y);
                    ShowCaret(rtb.Handle);
                }
                catch { }
            }));
        }

        static float Lum(Color c) { return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f; }

        Label Tool(string t, string tip, FontStyle fs)
        {
            var l = new Label { Text = t, AutoSize = true, Font = new Font(small.FontFamily, small.Size * (t.Length == 1 ? 1.15f : 1f), fs, GraphicsUnit.Pixel), Cursor = Cursors.Hand, Padding = new Padding(6, 3, 6, 3), TextAlign = ContentAlignment.MiddleCenter };
            if (t.Length == 1) { l.Font = new Font("Georgia", small.Size * 1.2f, fs | (t == "B" ? FontStyle.Bold : 0), GraphicsUnit.Pixel); }
            new ToolTip().SetToolTip(l, tip);
            return l;
        }

        float BaseEm { get { return Math.Max(10, S.MemoSize) * s; } }

        FontFamily Fam
        {
            get
            {
                try { return string.IsNullOrEmpty(S.MemoFont) ? Painter.FamFor(S) : Painter.HudFam(S.MemoFont); }
                catch { return Painter.UiFam; }
            }
        }

        Font MakeFont(float em, FontStyle st)
        {
            var ff = Fam;
            // 이 글꼴에 없는 꾸미기는 빼고 (굵게·기울임이 없는 손글씨 글꼴 등)
            foreach (var t in new[] { st, st & ~FontStyle.Italic, st & ~FontStyle.Bold, st & ~(FontStyle.Bold | FontStyle.Italic), FontStyle.Regular, FontStyle.Bold })
                if (ff.IsStyleAvailable(t)) { try { return new Font(ff, em, t, GraphicsUnit.Pixel); } catch { } }
            return new Font(Painter.UiFam, em, st, GraphicsUnit.Pixel);
        }

        // ---------- 글 ↔ 꾸미기 덩어리 ----------
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        void Freeze(bool on) { if (rtb.IsHandleCreated) SendMessage(rtb.Handle, 0x000B /*WM_SETREDRAW*/, new IntPtr(on ? 0 : 1), IntPtr.Zero); if (!on) rtb.Invalidate(); }

        void Load(List<MemoRun> runs, bool keepSel)
        {
            int ss = rtb.SelectionStart, sl = rtb.SelectionLength;
            loading = true; Freeze(true);
            try
            {
                rtb.Clear();
                foreach (var r in runs)
                {
                    string t = (r.T ?? "").Replace("\r", "");
                    if (t.Length == 0) continue;
                    rtb.Select(rtb.TextLength, 0);
                    var st = (r.B ? FontStyle.Bold : 0) | (r.I ? FontStyle.Italic : 0) | (r.U ? FontStyle.Underline : 0) | (r.K ? FontStyle.Strikeout : 0);
                    rtb.SelectionFont = MakeFont(BaseEm * (r.Z <= 0 ? 1 : r.Z), st);
                    rtb.SelectionColor = string.IsNullOrEmpty(r.C) ? fg : Painter.ParseColor(r.C);
                    rtb.SelectedText = t;
                }
                if (rtb.TextLength == 0) { rtb.SelectionFont = MakeFont(BaseEm, S.MemoBold ? FontStyle.Bold : FontStyle.Regular); rtb.SelectionColor = fg; }
                CenterAll();
                if (keepSel) rtb.Select(Math.Min(ss, rtb.TextLength), Math.Min(sl, Math.Max(0, rtb.TextLength - ss)));
                else rtb.Select(rtb.TextLength, 0);
            }
            finally { Freeze(false); loading = false; }
            ShowState();
        }

        void CenterAll()
        {
            int ss = rtb.SelectionStart, sl = rtb.SelectionLength;
            bool was = loading; loading = true;
            rtb.SelectAll(); rtb.SelectionAlignment = HorizontalAlignment.Center; rtb.Select(ss, sl);
            loading = was;
        }

        // 글자 하나하나의 꾸미기를 읽어 같은 것끼리 묶기
        public List<MemoRun> Runs
        {
            get
            {
                var list = new List<MemoRun>();
                int n = rtb.TextLength;
                if (n == 0) return list;
                int ss = rtb.SelectionStart, sl = rtb.SelectionLength;
                bool was = loading; loading = true; Freeze(true);
                try
                {
                    string text = rtb.Text;
                    MemoRun cur = null; var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < n; i++)
                    {
                        rtb.Select(i, 1);
                        var f = rtb.SelectionFont; var c = rtb.SelectionColor;
                        var r = new MemoRun
                        {
                            B = f != null && f.Bold, I = f != null && f.Italic, U = f != null && f.Underline, K = f != null && f.Strikeout,
                            C = c.ToArgb() == fg.ToArgb() ? null : "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"),
                            Z = f == null ? 1 : (float)Math.Round(f.SizeInPoints / PxToPt(BaseEm), 2)
                        };
                        if (Math.Abs(r.Z - 1) < 0.04f) r.Z = 1;
                        if (cur != null && cur.SameStyle(r)) sb.Append(text[i]);
                        else
                        {
                            if (cur != null) { cur.T = sb.ToString(); list.Add(cur); }
                            cur = r; sb.Clear(); sb.Append(text[i]);
                        }
                    }
                    if (cur != null) { cur.T = sb.ToString(); list.Add(cur); }
                    rtb.Select(ss, sl);
                }
                finally { Freeze(false); loading = was; }
                return list;
            }
        }

        float PxOf(Font f) { return f.SizeInPoints / PxToPt(1f); }
        float PxToPt(float px) { using (var f = new Font(Painter.UiFam, px, GraphicsUnit.Pixel)) return f.SizeInPoints; }

        // 고른 부분(없으면 전체)의 글자마다 꾸미기를 바꿈
        void Each(Func<Font, Font> font, Color? color)
        {
            bool whole = rtb.SelectionLength == 0;
            int ss = rtb.SelectionStart, sl = rtb.SelectionLength;
            int a = whole ? 0 : ss, n = whole ? rtb.TextLength : sl;
            loading = true; Freeze(true);
            try
            {
                for (int i = a; i < a + n; i++)
                {
                    rtb.Select(i, 1);
                    if (font != null && rtb.SelectionFont != null) rtb.SelectionFont = font(rtb.SelectionFont);
                    if (color.HasValue) rtb.SelectionColor = color.Value;
                }
                // 다음에 쓸 글자도 같은 꾸미기로
                if (whole)
                {
                    rtb.Select(ss, 0);
                    if (font != null && rtb.SelectionFont != null) rtb.SelectionFont = font(rtb.SelectionFont);
                    if (color.HasValue) rtb.SelectionColor = color.Value;
                }
                else rtb.Select(ss, sl);
            }
            finally { Freeze(false); loading = false; }
            if (Changed != null) Changed(MemoText);
            ShowState(); rtb.Focus();
        }

        void Toggle(FontStyle st)
        {
            bool whole = rtb.SelectionLength == 0;
            Font cur;
            if (whole) { int ss = rtb.SelectionStart; loading = true; rtb.SelectAll(); cur = rtb.SelectionFont; rtb.Select(ss, 0); loading = false; }
            else cur = rtb.SelectionFont;
            bool on = cur == null || (cur.Style & st) == 0;   // 섞여 있으면 켜기
            if (whole && st == FontStyle.Bold) S.MemoBold = on;
            Each(f => MakeFont(PxOf(f), on ? (f.Style | st) : (f.Style & ~st)), null);
        }

        void ResizeSel(int d)
        {
            if (rtb.SelectionLength == 0)
            {
                var runs = Runs;
                S.MemoSize = Math.Max(10, Math.Min(60, S.MemoSize + d));
                Load(runs, true);
                if (Changed != null) Changed(MemoText);
                rtb.Focus();
                return;
            }
            float step = d * s;
            Each(f => MakeFont(Math.Max(8 * s, Math.Min(120 * s, PxOf(f) + step)), f.Style), null);
        }

        void PickColor()
        {
            holdOpen = true;
            Color start = rtb.SelectionLength > 0 ? rtb.SelectionColor : Painter.ParseColor(S.MemoColor);
            Color c;
            bool ok = ColorPicker.Pick(this, start, out c);
            holdOpen = false; Activate();
            if (!ok) { rtb.Focus(); return; }
            if (rtb.SelectionLength == 0)
            {
                // 전체: 기본 글자색을 바꾸고 부분 색은 지움
                var runs = Runs; foreach (var r in runs) r.C = null;
                S.MemoColor = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                ApplyLook(); Load(runs, true);
                if (Changed != null) Changed(MemoText);
                rtb.Focus();
            }
            else Each(null, c);
        }

        // 버튼에 지금 고른 글자의 꾸미기 표시
        void ShowState()
        {
            Font f = rtb.SelectionFont;
            Color muted = Lum(bg) > 0.5f ? Color.FromArgb(110, 116, 135) : Color.FromArgb(205, 210, 225);
            bBold.ForeColor = f != null && f.Bold ? Ui.Accent : muted;
            bItalic.ForeColor = f != null && f.Italic ? Ui.Accent : muted;
            bUnder.ForeColor = f != null && f.Underline ? Ui.Accent : muted;
            bStrike.ForeColor = f != null && f.Strikeout ? Ui.Accent : muted;
            bColor.Swatch = rtb.SelectionLength > 0 || rtb.TextLength > 0 ? rtb.SelectionColor : fg;
            bColor.ForeColor = muted;
            bColor.Invalidate();
        }

        // 바탕·기본 글자색
        void ApplyLook()
        {
            bg = S.MemoBgOn ? Painter.ParseColor(S.MemoBgColor) : Color.FromArgb(250, 250, 253);
            fg = Painter.ParseColor(S.MemoColor);
            if (Math.Abs(Lum(bg) - Lum(fg)) < 0.35f) fg = Lum(bg) > 0.5f ? Color.FromArgb(34, 38, 52) : Color.White;
            BackColor = bg; rtb.BackColor = bg; rtb.ForeColor = fg; hint.BackColor = bg;
            Color muted = Lum(bg) > 0.5f ? Color.FromArgb(110, 116, 135) : Color.FromArgb(205, 210, 225);
            hint.ForeColor = muted;
            foreach (var c in bar)
            {
                var l = c as Label; if (l == null) { if (c is ColorA) c.BackColor = bg; continue; }
                l.BackColor = bg; l.ForeColor = l == bDone ? Ui.Accent : muted;
            }
            foreach (var d in dots) { d.BackColor = bg; d.Selected = d.Hex == "" ? !S.MemoBgOn : (S.MemoBgOn && string.Equals(d.Hex, S.MemoBgColor, StringComparison.OrdinalIgnoreCase)); d.Invalidate(); }
            Invalidate(true);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.B)) { Toggle(FontStyle.Bold); return true; }
            if (keyData == (Keys.Control | Keys.I)) { Toggle(FontStyle.Italic); return true; }
            if (keyData == (Keys.Control | Keys.U)) { Toggle(FontStyle.Underline); return true; }
            if (keyData == Keys.Escape || keyData == (Keys.Control | Keys.Enter)) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        int centerX, top, minW;
        public void Place(Rectangle content, int y)
        {
            int mw = Math.Max(content.Width, (int)(500 * Math.Max(0.85f, s)));
            int cx = content.Left + content.Width / 2;
            if (mw == minW && cx == centerX && y == top) return;   // 그대로면 아무것도 안 함 (깜빡임 방지)
            minW = mw; centerX = cx; top = y;
            Fit();
        }

        Rectangle lastBounds;
        void Fit()
        {
            if (minW == 0 || rtb == null) return;
            int pad = (int)(12 * s), gap = (int)(4 * Math.Max(0.85f, s));
            int w = minW, x = pad, y = (int)(8 * s), rowH = 0;
            foreach (var c in bar)
            {
                int cw = c is Label ? ((Label)c).PreferredWidth : c.Width, chh = c is Label ? ((Label)c).PreferredHeight : c.Height;
                if (x + cw > w - pad && x > pad) { x = pad; y += rowH + gap; rowH = 0; }
                rowH = Math.Max(rowH, Math.Max(chh, cbFont.Height));
                c.Tag = new Rectangle(x, y, cw, chh);
                x += cw + gap + (c == bPlus || c == bColor || c == dots[dots.Count - 1] ? gap * 2 : 0);
            }
            int rh = Math.Max(rowH, cbFont.Height);
            foreach (var c in bar)
            {
                var r = (Rectangle)c.Tag;
                var nb = new Rectangle(r.X, r.Y + (rh - r.Height) / 2, r.Width, r.Height);
                if (c.Bounds != nb) c.Bounds = nb;
            }
            int barBottom = y + rh + (int)(6 * s);

            int lineH = (int)Math.Ceiling(BaseEm * 1.35f);
            int th = Math.Max(lineH, Math.Min(lineH * 12, contentH > 0 ? contentH + 2 : lineH));
            int textW = w - pad * 2;
            int hintH = hint.Visible ? (int)Math.Ceiling(small.GetHeight() + 6 * s) : 0;
            int h = barBottom + (int)(4 * s) + th + hintH + pad;
            var nbForm = new Rectangle(centerX - w / 2, top, w, h);
            var tbR = new Rectangle(pad, barBottom + (int)(4 * s), textW, th);
            if (rtb.Bounds != tbR) rtb.Bounds = tbR;
            var hR = new Rectangle(pad, tbR.Bottom, textW, Math.Max(1, hintH));
            if (hint.Bounds != hR) hint.Bounds = hR;
            lineY = barBottom;
            if (nbForm != lastBounds)
            {
                bool resized = nbForm.Size != lastBounds.Size;
                lastBounds = nbForm;
                Bounds = nbForm;
                if (resized) { using (var p = Painter.RoundPath(new RectangleF(0, 0, w, h), 14 * s)) Region = new Region(p); Invalidate(); }
            }
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

        // 글자색 버튼: A 아래에 지금 색 막대
        class ColorA : Control
        {
            public Color Swatch = Color.Black;
            public ColorA() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
                var r = new Rectangle(0, 0, Width, (int)(Height * 0.78f));
                TextRenderer.DrawText(g, "A", Font, r, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int bh = Math.Max(3, Height / 7);
                using (var b = new SolidBrush(Swatch)) g.FillRectangle(b, Width * 0.18f, Height - bh - 2, Width * 0.64f, bh);
            }
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
