using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 원하는 색을 직접 고르는 창: 색 판 + 색상 막대 + 색 코드(#RRGGBB)
    public class ColorPicker : GlassForm
    {
        public Color Result;
        float h, s, v;
        readonly PaintBox sv, hue;
        readonly Swatch newSw, oldSw;
        readonly TextBox hex;
        bool updating;
        static readonly System.Collections.Generic.List<Color> recent = new System.Collections.Generic.List<Color>();
        static readonly string[] Common = { "#FFFFFF", "#000000", "#FF3B3B", "#FF8A1F", "#FFC61A", "#1DB863", "#1F5FFF", "#8F5CFF",
                                            "#FF5FA2", "#3EF2FF", "#14213D", "#7A8BA8", "#FFF3BF", "#2F5D46", "#A07A45", "#E6ECF5" };

        public static bool Pick(IWin32Window owner, Color init, out Color result)
        {
            using (var f = new ColorPicker(init))
            {
                if (f.ShowDialog(owner) == DialogResult.OK)
                {
                    result = f.Result;
                    recent.Remove(result); recent.Insert(0, result);
                    if (recent.Count > 8) recent.RemoveAt(8);
                    return true;
                }
            }
            result = init;
            return false;
        }

        public ColorPicker(Color init)
        {
            Result = init;
            ToHsv(init, out h, out s, out v);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Media.UiFont(10F);
            Text = "색 고르기";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 510);
            Padding = new Padding(12);

            var card = new GlassPanel { Dock = DockStyle.Fill, Alpha = 140 };
            Controls.Add(card);

            sv = new PaintBox { Location = new Point(18, 18), Size = new Size(280, 220), Cursor = Cursors.Cross };
            sv.Paint += PaintSv;
            MouseEventHandler svMouse = (o, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                s = Clamp(e.X / (float)(sv.Width - 1)); v = 1 - Clamp(e.Y / (float)(sv.Height - 1));
                Update2(true);
            };
            sv.MouseDown += svMouse; sv.MouseMove += svMouse;
            card.Controls.Add(sv);

            hue = new PaintBox { Location = new Point(18, 250), Size = new Size(280, 22) };
            hue.Paint += PaintHue;
            MouseEventHandler hueMouse = (o, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                h = Clamp(e.X / (float)(hue.Width - 1)) * 360f;
                Update2(true);
            };
            hue.MouseDown += hueMouse; hue.MouseMove += hueMouse;
            card.Controls.Add(hue);

            card.Controls.Add(new Label { Text = "새 색", Location = new Point(316, 18), AutoSize = true, ForeColor = Ui.Muted, BackColor = Color.Transparent });
            newSw = new Swatch { Location = new Point(314, 40), Size = new Size(86, 70), Value = init, Cursor = Cursors.Default };
            card.Controls.Add(newSw);
            card.Controls.Add(new Label { Text = "원래 색", Location = new Point(316, 120), AutoSize = true, ForeColor = Ui.Muted, BackColor = Color.Transparent });
            oldSw = new Swatch { Location = new Point(314, 142), Size = new Size(86, 50), Value = init };
            oldSw.Click += (o, e) => { ToHsv(oldSw.Value, out h, out s, out v); Update2(true); };
            card.Controls.Add(oldSw);

            card.Controls.Add(new Label { Text = "색 코드", Location = new Point(18, 290), AutoSize = true, ForeColor = Ui.Muted, BackColor = Color.Transparent });
            hex = new TextBox { Location = new Point(80, 286), Width = 110, Font = Media.UiFont(11F), CharacterCasing = CharacterCasing.Upper };
            hex.TextChanged += (o, e) =>
            {
                if (updating) return;
                Color c;
                if (TryHex(hex.Text, out c)) { ToHsv(c, out h, out s, out v); Update2(false); }
            };
            card.Controls.Add(hex);
            card.Controls.Add(new Label { Text = "예: #FF8A1F", Location = new Point(200, 290), AutoSize = true, ForeColor = Ui.Muted, BackColor = Color.Transparent });

            card.Controls.Add(new Label { Text = "자주 쓰는 색" + (recent.Count > 0 ? " · 최근" : ""), Location = new Point(18, 324), AutoSize = true, ForeColor = Ui.Muted, BackColor = Color.Transparent });
            var flow = new FlowLayoutPanel { Location = new Point(14, 344), Size = new Size(390, 72), BackColor = Color.Transparent };
            var list = new System.Collections.Generic.List<Color>(recent);
            foreach (var cs in Common) { var c = ColorTranslator.FromHtml(cs); if (!list.Contains(c)) list.Add(c); }
            for (int i = 0; i < list.Count && i < 24; i++)
            {
                var sw = new Swatch { Value = list[i], Size = new Size(30, 30), Margin = new Padding(0, 0, 4, 4) };
                sw.Click += (o, e) => { ToHsv(((Swatch)o).Value, out h, out s, out v); Update2(true); };
                flow.Controls.Add(sw);
            }
            card.Controls.Add(flow);

            var bOk = new GButton { Text = "이 색으로", Location = new Point(194, 432), Size = new Size(110, 38), Tag = "primary", DialogResult = DialogResult.OK };
            var bCancel = new GButton { Text = "취소", Location = new Point(312, 432), Size = new Size(90, 38), DialogResult = DialogResult.Cancel };
            card.Controls.Add(bOk); card.Controls.Add(bCancel);
            AcceptButton = bOk; CancelButton = bCancel;
            Ui.Apply(this);
            Update2(true);
        }

        static float Clamp(float x) { return Math.Max(0, Math.Min(1, x)); }

        void Update2(bool setHex)
        {
            Result = FromHsv(h, s, v);
            newSw.Value = Result;
            if (setHex) { updating = true; hex.Text = "#" + Result.R.ToString("X2") + Result.G.ToString("X2") + Result.B.ToString("X2"); updating = false; }
            sv.Invalidate(); hue.Invalidate();
        }

        void PaintSv(object o, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, sv.Width - 1, sv.Height - 1);
            using (var clip = Ui.Round(r, 10))
            {
                g.SetClip(clip);
                using (var b = new SolidBrush(FromHsv(h, 1, 1))) g.FillRectangle(b, r);
                using (var b = new LinearGradientBrush(r, Color.White, Color.FromArgb(0, 255, 255, 255), 0f)) g.FillRectangle(b, r);
                using (var b = new LinearGradientBrush(r, Color.FromArgb(0, 0, 0, 0), Color.Black, 90f)) g.FillRectangle(b, r);
                g.ResetClip();
                using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f)) g.DrawPath(pen, clip);
            }
            float x = s * (sv.Width - 1), y = (1 - v) * (sv.Height - 1);
            using (var pen = new Pen(Color.White, 2.5f)) g.DrawEllipse(pen, x - 8, y - 8, 16, 16);
            using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1f)) g.DrawEllipse(pen, x - 9.5f, y - 9.5f, 19, 19);
        }

        void PaintHue(object o, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 3, hue.Width - 1, hue.Height - 7);
            using (var p = Ui.Round(r, r.Height / 2f))
            using (var b = new LinearGradientBrush(r, Color.Red, Color.Red, 0f))
            {
                var cb = new ColorBlend(7);
                cb.Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red };
                cb.Positions = new[] { 0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f };
                b.InterpolationColors = cb;
                g.FillPath(b, p);
            }
            float x = h / 360f * (hue.Width - 1);
            var k = new RectangleF(x - 8, hue.Height / 2f - 9, 16, 18);
            using (var p = Ui.Round(k, 6))
            {
                using (var b = new SolidBrush(FromHsv(h, 1, 1))) g.FillPath(b, p);
                using (var pen = new Pen(Color.White, 2.5f)) g.DrawPath(pen, p);
            }
        }

        public static bool TryHex(string t, out Color c)
        {
            c = Color.Empty;
            t = (t ?? "").Trim().TrimStart('#');
            if (t.Length == 3) t = "" + t[0] + t[0] + t[1] + t[1] + t[2] + t[2];
            int n;
            if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) return false;
            c = Color.FromArgb((n >> 16) & 255, (n >> 8) & 255, n & 255);
            return true;
        }

        public static void ToHsv(Color c, out float h, out float s, out float v)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max; s = max <= 0 ? 0 : d / max;
            if (d <= 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        public static Color FromHsv(float h, float s, float v)
        {
            float c = v * s, x = c * (1 - Math.Abs((h / 60f) % 2 - 1)), m = v - c;
            float r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }
    }
}
