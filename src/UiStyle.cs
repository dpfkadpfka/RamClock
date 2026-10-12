using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 글래스모피즘 디자인: 파스텔 배경 위에 반투명 유리 카드
    public static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(246, 247, 255);
        public static readonly Color Side = Color.FromArgb(238, 241, 252);
        public static readonly Color Line = Color.FromArgb(214, 221, 240);
        public static readonly Color Ink = Color.FromArgb(28, 34, 58);
        public static readonly Color Muted = Color.FromArgb(104, 112, 140);
        public static readonly Color Accent = Color.FromArgb(76, 99, 255);
        public static readonly Color Accent2 = Color.FromArgb(160, 92, 255);
        public static readonly Color AccentSoft = Color.FromArgb(232, 236, 255);
        public static readonly Color Field = Color.FromArgb(252, 252, 255);

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

        // 새 창을 다른 창(시계 포함)보다 맨 앞으로 올리기
        public static void BringUp(Form f)
        {
            try
            {
                bool was = f.TopMost;
                f.TopMost = true;
                f.BringToFront();
                f.Activate();
                SetForegroundWindow(f.Handle);
                if (!was) f.TopMost = false;
            }
            catch { }
        }

        public static void Apply(Control c)
        {
            if (c is Form) StyleForm((Form)c);
            foreach (Control ch in c.Controls) Apply(ch);
            if (c is DataGridView) Grid((DataGridView)c);
            else if (c is TextBox) { var t = (TextBox)c; t.BorderStyle = BorderStyle.FixedSingle; t.BackColor = Field; t.ForeColor = Ink; }
            else if (c is ComboBox) { var cb = (ComboBox)c; cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Field; cb.ForeColor = Ink; }
            else if (c is NumericUpDown) { c.BackColor = Field; ((NumericUpDown)c).BorderStyle = BorderStyle.FixedSingle; }
            else if (c is Label) { c.BackColor = Color.Transparent; if (c.ForeColor == SystemColors.ControlText) c.ForeColor = Ink; }
            else if (c is CheckBox || c is RadioButton) c.BackColor = Color.Transparent;
            else if (c is TableLayoutPanel || c is FlowLayoutPanel || (c is Panel && !(c is GlassPanel)) || c is TabPage)
            {
                if (c.BackColor == SystemColors.Control || c.BackColor == Color.White || c.BackColor == Bg || c.BackColor == Side) c.BackColor = Color.Transparent;
            }
            Modernize(c);
        }

        // 윈도우 10/11 최신 스크롤바
        public static void Modernize(Control c)
        {
            if (c is ScrollableControl || c is DataGridView || c is ListBox || c is TextBox)
            {
                if (c.IsHandleCreated) Theme(c);
                else c.HandleCreated += (o, e) => Theme((Control)o);
            }
        }
        static void Theme(Control c) { try { SetWindowTheme(c.Handle, "Explorer", null); } catch { } }

        public static void StyleForm(Form f)
        {
            f.BackColor = Bg;
            f.ForeColor = Ink;
            f.BackgroundImageLayout = ImageLayout.None;
            EventHandler regen = (o, e) =>
            {
                if (f.ClientSize.Width < 10 || f.ClientSize.Height < 10) return;
                var old = f.BackgroundImage;
                f.BackgroundImage = Backdrop(f.ClientSize.Width, f.ClientSize.Height);
                if (old != null) old.Dispose();
            };
            f.Resize += regen;
            regen(f, EventArgs.Empty);
            EventHandler chrome = (o, e) =>
            {
                try
                {
                    int round = 2; DwmSetWindowAttribute(f.Handle, 33, ref round, 4);          // 둥근 모서리 (윈도우 11)
                    int cap = 0x00FFEFEA; DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);   // 제목 표시줄 색 (BGR)
                    int txt = 0x003A221C; DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);
                }
                catch { }
            };
            if (f.IsHandleCreated) chrome(f, EventArgs.Empty); else f.HandleCreated += chrome;
        }

        // 파스텔 그라데이션 + 부드러운 빛 덩어리
        public static Bitmap Backdrop(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(234, 239, 255), Color.FromArgb(247, 236, 255), 35f)) g.FillRectangle(b, 0, 0, w, h);
                Blob(g, w * 0.08f, h * 0.10f, Math.Max(w, h) * 0.42f, Color.FromArgb(150, 140, 175, 255));
                Blob(g, w * 0.92f, h * 0.18f, Math.Max(w, h) * 0.38f, Color.FromArgb(130, 214, 170, 255));
                Blob(g, w * 0.70f, h * 0.95f, Math.Max(w, h) * 0.45f, Color.FromArgb(120, 255, 186, 214));
                Blob(g, w * 0.20f, h * 0.90f, Math.Max(w, h) * 0.32f, Color.FromArgb(110, 140, 230, 255));
            }
            return bmp;
        }
        static void Blob(Graphics g, float cx, float cy, float r, Color c)
        {
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                using (var pb = new PathGradientBrush(p) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } }) g.FillPath(pb, p);
            }
        }

        public static void Grid(DataGridView g)
        {
            g.BorderStyle = BorderStyle.None;
            g.BackgroundColor = Color.FromArgb(250, 251, 255);
            g.GridColor = Color.FromArgb(230, 234, 246);
            g.EnableHeadersVisualStyles = false;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 243, 253);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(240, 243, 253);
            g.ColumnHeadersDefaultCellStyle.Font = Media.UiFont(9.5F, true);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 8, 6, 8);
            g.DefaultCellStyle.BackColor = Color.FromArgb(250, 251, 255);
            g.DefaultCellStyle.SelectionBackColor = AccentSoft;
            g.DefaultCellStyle.SelectionForeColor = Ink;
            g.DefaultCellStyle.ForeColor = Ink;
            if (g.DefaultCellStyle.Padding.Left < 4) g.DefaultCellStyle.Padding = new Padding(8, 4, 6, 4);
            g.RowTemplate.Height = Math.Max(g.RowTemplate.Height, 38);
            foreach (DataGridViewRow r in g.Rows) if (r.Height < 38) r.Height = 38;
            g.RowsAdded += (o, e) =>
            {
                for (int i = e.RowIndex; i < e.RowIndex + e.RowCount && i < g.Rows.Count; i++) if (g.Rows[i].Height < 38) g.Rows[i].Height = 38;
            };
            g.CellPainting += (o, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var col = g.Columns[e.ColumnIndex];
                if (col is DataGridViewButtonColumn)
                {
                    e.PaintBackground(e.CellBounds, false);
                    var r = new RectangleF(e.CellBounds.X + 4, e.CellBounds.Y + 6, e.CellBounds.Width - 8, e.CellBounds.Height - 12);
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var p = Round(r, r.Height / 2))
                    {
                        using (var b = new SolidBrush(Color.White)) e.Graphics.FillPath(b, p);
                        using (var pen = new Pen(Color.FromArgb(205, 212, 240))) e.Graphics.DrawPath(pen, p);
                    }
                    using (var f = Media.UiFont(9F, true))
                        TextRenderer.DrawText(e.Graphics, ((DataGridViewButtonColumn)col).Text ?? "", f, Rectangle.Round(r), Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    e.Handled = true;
                }
                else if (col is DataGridViewCheckBoxColumn)
                {
                    e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                    bool on = e.Value is bool && (bool)e.Value;
                    float tw = 34, th = 18;
                    var r = new RectangleF(e.CellBounds.X + (e.CellBounds.Width - tw) / 2, e.CellBounds.Y + (e.CellBounds.Height - th) / 2, tw, th);
                    DrawSwitch(e.Graphics, r, on, true);
                    e.Handled = true;
                }
            };
        }

        public static void DrawSwitch(Graphics g, RectangleF r, bool on, bool enabled)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Round(r, r.Height / 2))
            {
                if (on) using (var b = new LinearGradientBrush(r, enabled ? Accent : Color.Silver, enabled ? Accent2 : Color.Silver, 0f)) g.FillPath(b, p);
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(190, 196, 220))) g.DrawPath(pen, p);
                }
            }
            float d = r.Height - 4;
            var k = new RectangleF(on ? r.Right - d - 2 : r.X + 2, r.Y + 2, d, d);
            using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 40))) g.FillEllipse(sh, k.X, k.Y + 1, d, d);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, k);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Max(1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        public static void ParentBg(Control c, PaintEventArgs e)
        {
            if (c.Parent == null) return;
            var st = e.Graphics.Save();
            e.Graphics.TranslateTransform(-c.Left, -c.Top);
            var pe = new PaintEventArgs(e.Graphics, new Rectangle(c.Left, c.Top, c.Width, c.Height));
            InvokeBg(c.Parent, pe);
            e.Graphics.Restore(st);
        }
        static readonly System.Reflection.MethodInfo opb = typeof(Control).GetMethod("OnPaintBackground", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        static void InvokeBg(Control parent, PaintEventArgs pe)
        {
            try { opb.Invoke(parent, new object[] { pe }); } catch { }
        }
    }

    // 반투명 유리 카드 (배경을 미리 그려 두어 빠르게)
    public class GlassPanel : Panel
    {
        public float Radius = 18;
        public int Alpha = 120;
        Bitmap cache;
        public GlassPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }
        void Drop() { if (cache != null) { cache.Dispose(); cache = null; } }
        protected override void OnResize(EventArgs e) { Drop(); base.OnResize(e); }
        protected override void OnLocationChanged(EventArgs e) { Drop(); base.OnLocationChanged(e); }
        protected override void OnParentChanged(EventArgs e) { Drop(); base.OnParentChanged(e); }
        protected override void OnParentBackgroundImageChanged(EventArgs e) { Drop(); Invalidate(true); base.OnParentBackgroundImageChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) Drop(); base.Dispose(disposing); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            if (cache == null)
            {
                cache = new Bitmap(Width, Height);
                using (var g = Graphics.FromImage(cache))
                {
                    Ui.ParentBg(this, new PaintEventArgs(g, new Rectangle(0, 0, Width, Height)));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                    using (var p = Ui.Round(r, Radius))
                    {
                        using (var b = new LinearGradientBrush(r, Color.FromArgb(Math.Min(255, Alpha + 30), 255, 255, 255), Color.FromArgb(Alpha, 255, 255, 255), 90f)) g.FillPath(b, p);
                        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.2f)) g.DrawPath(pen, p);
                    }
                }
            }
            e.Graphics.DrawImageUnscaled(cache, 0, 0);
        }
    }

    // 둥근 유리 버튼 (primary: 그라데이션)
    public class GButton : Button
    {
        bool hover, down;
        public GButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            MinimumSize = new Size(0, 36);
        }
        bool Primary { get { return Tag as string == "primary"; } }
        public override Size GetPreferredSize(Size proposed)
        {
            var t = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(Math.Max(MinimumSize.Width, t.Width + 34), Math.Max(MinimumSize.Height, t.Height + 14));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.ParentBg(this, e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            if (down) r.Inflate(-1, -1);
            float rad = Math.Min(12, r.Height / 2);
            using (var p = Ui.Round(r, rad))
            {
                if (Primary)
                {
                    Color a = Enabled ? Ui.Accent : Color.Silver, b = Enabled ? Ui.Accent2 : Color.Silver;
                    if (hover) { a = ControlPaint.Light(a, 0.15f); b = ControlPaint.Light(b, 0.15f); }
                    using (var sh = Ui.Round(new RectangleF(r.X + 1, r.Y + 3, r.Width - 2, r.Height - 2), rad)) using (var sb = new SolidBrush(Color.FromArgb(50, 76, 99, 255))) g.FillPath(sb, sh);
                    using (var br = new LinearGradientBrush(r, a, b, 0f)) g.FillPath(br, p);
                    using (var gl = new LinearGradientBrush(r, Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f)) g.FillPath(gl, p);
                }
                else
                {
                    using (var br = new SolidBrush(Color.FromArgb(hover ? 235 : 185, 255, 255, 255))) g.FillPath(br, p);
                    using (var pen = new Pen(hover ? Color.FromArgb(170, 76, 99, 255) : Color.FromArgb(230, 255, 255, 255), 1.2f)) g.DrawPath(pen, p);
                    using (var pen2 = Ui.Round(new RectangleF(r.X, r.Y, r.Width, r.Height), rad)) using (var pn = new Pen(Color.FromArgb(40, 60, 70, 140))) g.DrawPath(pn, pen2);
                }
            }
            Color tc = Primary ? Color.White : (Enabled ? Ui.Ink : Color.FromArgb(160, 166, 186));
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), tc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) using (var p = Ui.Round(new RectangleF(r.X - 0.5f, r.Y - 0.5f, r.Width + 1, r.Height + 1), rad)) using (var pen = new Pen(Color.FromArgb(160, Ui.Accent), 1.5f)) g.DrawPath(pen, p);
        }
    }

    // 스위치 모양 체크박스
    public class Toggle : CheckBox
    {
        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }
        public override Size GetPreferredSize(Size proposed)
        {
            var t = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(44 + 8 + t.Width + 4, Math.Max(30, t.Height + 8));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.ParentBg(this, e);
            var g = e.Graphics;
            float th = 22, tw = 40;
            var r = new RectangleF(1, (Height - th) / 2, tw, th);
            Ui.DrawSwitch(g, r, Checked, Enabled);
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(tw + 10), 0, Width - (int)tw - 10, Height), Enabled ? Ui.Ink : Ui.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // 알약 모양 선택 칩
    public class Chip : RadioButton
    {
        bool hover;
        public Chip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }
        public override Size GetPreferredSize(Size proposed)
        {
            var t = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(t.Width + 30, Math.Max(32, t.Height + 12));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.ParentBg(this, e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var p = Ui.Round(r, r.Height / 2))
            {
                if (Checked) using (var b = new LinearGradientBrush(r, Ui.Accent, Ui.Accent2, 0f)) g.FillPath(b, p);
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb(hover ? 235 : 170, 255, 255, 255))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(205, 212, 240))) g.DrawPath(pen, p);
                }
            }
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), Checked ? Color.White : Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // 새 슬라이더
    public class Slider : Control
    {
        int min, max = 100, val;
        bool drag;
        public event EventHandler ValueChanged;
        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Invalidate(); } }
        public int Value
        {
            get { return val; }
            set { int v = Math.Max(min, Math.Min(max, value)); if (v == val) return; val = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        }
        public Slider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Size = new Size(140, 30);
            Cursor = Cursors.Hand;
        }
        float Frac { get { return max > min ? (val - min) / (float)(max - min) : 0; } }
        void SetFromX(int x) { float f = Math.Max(0, Math.Min(1, (x - 9) / (float)Math.Max(1, Width - 18))); Value = min + (int)Math.Round(f * (max - min)); }
        protected override void OnMouseDown(MouseEventArgs e) { drag = true; Capture = true; SetFromX(e.X); Focus(); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { Value += e.Delta > 0 ? 1 : -1; base.OnMouseWheel(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.ParentBg(this, e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float cy = Height / 2f, x0 = 9, x1 = Width - 9, kx = x0 + (x1 - x0) * Frac;
            var track = new RectangleF(x0, cy - 3, x1 - x0, 6);
            using (var p = Ui.Round(track, 3)) using (var b = new SolidBrush(Color.FromArgb(160, 255, 255, 255))) g.FillPath(b, p);
            using (var p = Ui.Round(track, 3)) using (var pen = new Pen(Color.FromArgb(200, 206, 230))) g.DrawPath(pen, p);
            if (kx - x0 > 1) using (var p = Ui.Round(new RectangleF(x0, cy - 3, kx - x0, 6), 3)) using (var b = new LinearGradientBrush(new RectangleF(x0 - 1, cy - 3, x1 - x0 + 2, 6), Ui.Accent, Ui.Accent2, 0f)) g.FillPath(b, p);
            using (var sh = new SolidBrush(Color.FromArgb(50, 30, 40, 100))) g.FillEllipse(sh, kx - 8, cy - 7, 16, 16);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - 8, cy - 8, 16, 16);
            using (var pen = new Pen(Ui.Accent, 2)) g.DrawEllipse(pen, kx - 8, cy - 8, 16, 16);
        }
    }

    // 색 견본 (누르면 색 고르기)
    public class Swatch : Control
    {
        Color v = Color.White;
        public Color Value { get { return v; } set { v = value; Invalidate(); } }
        public Swatch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Size = new Size(36, 32); Cursor = Cursors.Hand;
        }
        public bool Selected, Editable;
        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.ParentBg(this, e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(2, 2, Width - 5, Height - 5);
            using (var p = Ui.Round(r, 9))
            {
                using (var b = new SolidBrush(v)) g.FillPath(b, p);
                using (var pen = new Pen(Selected ? Ui.Accent : Color.FromArgb(200, 206, 230), Selected ? 2.5f : 1.2f)) g.DrawPath(pen, p);
            }
            if (Editable)
            {
                // 오른쪽 아래 작은 연필 표시: 누르면 색을 고를 수 있다는 뜻
                var c = new RectangleF(Width - 17, Height - 17, 14, 14);
                using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, c);
                using (var pen = new Pen(Color.FromArgb(200, 206, 230))) g.DrawEllipse(pen, c);
                using (var pen = new Pen(Ui.Accent, 1.6f)) g.DrawLine(pen, c.X + 4, c.Bottom - 4, c.Right - 4, c.Y + 4);
            }
        }
    }

    // 그림 직접 그리는 칸 (테마 견본 등)
    public class PaintBox : Control
    {
        public PaintBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { Ui.ParentBg(this, e); }
    }

    // 왼쪽 메뉴
    public class NavBar : Control
    {
        public readonly List<string> Items = new List<string>();
        int sel = -1, hov = -1;
        public int ItemHeight = 46;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex
        {
            get { return sel; }
            set { if (value == sel) return; sel = value; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
        }
        public NavBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
        }
        int Hit(int y) { int i = y / ItemHeight; return i >= 0 && i < Items.Count ? i : -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Y); if (h != hov) { hov = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hov = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { int h = Hit(e.Y); if (h >= 0) SelectedIndex = h; base.OnMouseDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.ParentBg(this, e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Items.Count; i++)
            {
                var r = new RectangleF(4, i * ItemHeight + 4, Width - 8, ItemHeight - 8);
                bool s = i == sel;
                if (s || i == hov)
                    using (var p = Ui.Round(r, 12))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(s ? 225 : 120, 255, 255, 255))) g.FillPath(b, p);
                        if (s) using (var pen = new Pen(Color.FromArgb(240, 255, 255, 255), 1.2f)) g.DrawPath(pen, p);
                    }
                if (s) using (var b = new LinearGradientBrush(new RectangleF(r.X + 8, r.Y + 10, 4, r.Height - 20), Ui.Accent, Ui.Accent2, 90f))
                        using (var p = Ui.Round(new RectangleF(r.X + 8, r.Y + 10, 4, r.Height - 20), 2)) g.FillPath(b, p);
                using (var f = Media.UiFont(10.5F, s))
                    TextRenderer.DrawText(g, Items[i], f, Rectangle.Round(new RectangleF(r.X + 22, r.Y, r.Width - 22, r.Height)), s ? Ui.Accent : Ui.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }
    }
}

namespace ClassroomHud
{
    // 깜빡임 없이 한 번에 그리는 창
    public class GlassForm : System.Windows.Forms.Form
    {
        public GlassForm()
        {
            DoubleBuffered = true;
            SetStyle(System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer | System.Windows.Forms.ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override System.Windows.Forms.CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }   // WS_EX_COMPOSITED
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Ui.BringUp(this);
        }
    }
}
