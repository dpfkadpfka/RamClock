using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 랜덤 뽑기: 학생 카드 그리드 → 하이라이트가 돌다가 멈추고 → 뽑힌 카드를 크게
    public class PickerForm : Form
    {
        class Piece { public float X, Y, VX, VY, R, VR, W, H; public Color C; }
        class PBtn { public string Id, Text; public RectangleF R; public bool Primary, Round, On; }

        static readonly Random Rnd = new Random();
        // 파스텔 톤
        static readonly Color[] Border = {
            Color.FromArgb(255, 209, 222), Color.FromArgb(255, 226, 199), Color.FromArgb(255, 241, 186), Color.FromArgb(206, 240, 214),
            Color.FromArgb(198, 230, 250), Color.FromArgb(222, 214, 255), Color.FromArgb(242, 214, 250), Color.FromArgb(198, 238, 232) };
        static readonly Color Orange = Color.FromArgb(124, 92, 255);
        static Color Deep(Color c) { float h, s, v; ColorPicker.ToHsv(c, out h, out s, out v); return ColorPicker.FromHsv(h, Math.Min(1, s * 2.4f + 0.15f), v * 0.62f); }

        readonly Settings S;
        readonly Action save;
        readonly float k;
        readonly Timer anim;
        readonly List<Piece> confetti = new List<Piece>();
        readonly List<PBtn> btns = new List<PBtn>();
        readonly HashSet<int> absent = new HashSet<int>();
        int gFilter;                                   // 0 전체, 1 남학생, 2 여학생
        bool gEdit;                                    // 카드를 눌러 성별 정하기
        void SetGender(int no, string g)
        {
            var st = S.StudentOf(no);
            if (st == null) { st = new Student(no, ""); S.Students.Add(st); }
            st.Gender = g;
            if (save != null) save();
        }
        readonly List<RectangleF> filterR = new List<RectangleF>();
        bool HasGender() { foreach (var st in S.Students) if (st.Gender == "남" || st.Gender == "여") return true; return false; }
        bool GenderOk(int no)
        {
            if (gFilter == 0) return true;
            var st = S.StudentOf(no);
            return st != null && st.Gender == (gFilter == 1 ? "남" : "여");
        }
        readonly FontFamily fam, famB, famR;
        Font PF(float size, bool bold)
        {
            size = Math.Max(1f, size);
            var f = bold ? famB : famR;
            if (f != null) try { return new Font(f, size, FontStyle.Regular, GraphicsUnit.Pixel); } catch { }
            return new Font(fam, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }
        List<RectangleF> cards = new List<RectangleF>();
        Bitmap grid;           // 카드 그리드를 미리 그려 둔 그림
        Bitmap bigCard;        // 뽑힌 카드 큰 그림
        int phase;             // 0 고르는 중, 1 돌아가는 중, 2 발표
        int hi = -1, result = -1, lastPick = -1;
        DateTime t0, nextSwitch, lastFrame;
        List<int> pool;
        RectangleF closeBtn, maxBtn, minBtn;

        public PickerForm(Settings s, Action saveCb, float dpiK, Rectangle screen)
        {
            S = s; save = saveCb; k = Math.Max(1f, dpiK);
            fam = Painter.UiFam;
            // 뽑기 화면 글꼴: 프리젠테이션 (프로그램 안에 들어 있음)
            famB = BuiltinFonts.Get("★ 프리젠테이션");
            famR = BuiltinFonts.Get("★ 프리젠테이션 보통");
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;
            Text = "랜덤 뽑기";
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            KeyPreview = true;
            BackColor = Ui.Bg;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            int W = (int)(screen.Width * 0.92), H = (int)(screen.Height * 0.9);
            Bounds = new Rectangle(screen.X + (screen.Width - W) / 2, screen.Y + (screen.Height - H) / 2, W, H);

            anim = new Timer { Interval = 33 };
            anim.Tick += (o, e) => { try { Step(); } catch { } };
            KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Escape) { if (phase == 2) Back(); else Close(); }
                else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Act("draw");
            };
            Resize += (o, e) => { if (WindowState == FormWindowState.Minimized) return; Layout2(); if (phase == 2) MakeBigCard(); Invalidate(); };
            MinimumSize = new Size((int)(640 * k), (int)(420 * k));

        }

        public void ShowWithBackdrop() { Show(); Activate(); }
        public void DrawNow() { Act("draw"); }
        public Action OpenQr, OnRefresh;      // OnRefresh: 학생 카드 새로 받기                 // 학생 카드 꾸미기 QR 창 열기
        public void RefreshCards() { try { Layout2(); if (phase == 2) MakeBigCard(); Invalidate(); } catch { } }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Layout2(); Invalidate(); Ui.BringUp(this); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            anim.Stop(); anim.Dispose();
            if (grid != null) grid.Dispose();
            if (bigCard != null) bigCard.Dispose();
            base.OnFormClosed(e);
        }

        // ---------- 배치 ----------
        float TopH { get { return 64 * k; } }
        float BottomH { get { return 104 * k; } }

        void Layout2()
        {
            int n = Math.Max(1, S.StudentCount);
            float areaW = ClientSize.Width - 60 * k, areaH = ClientSize.Height - TopH - BottomH - 20 * k;
            float gap = 14 * k, best = 0; int bestC = 1;
            for (int c = 1; c <= n; c++)
            {
                int rows = (n + c - 1) / c;
                float w = Math.Min((areaW - gap * (c - 1)) / c, ((areaH - gap * (rows - 1)) / rows) / 1.18f);
                if (w > best) { best = w; bestC = c; }
            }
            best = Math.Min(best, 260 * k);
            float cw = best, ch = best * 1.18f;
            int cols = bestC, rowsN = (n + cols - 1) / cols;
            float totalH = rowsN * ch + (rowsN - 1) * gap;
            float y0 = TopH + 10 * k + (areaH - totalH) / 2;
            cards = new List<RectangleF>();
            for (int i = 0; i < n; i++)
            {
                int r = i / cols, c = i % cols;
                int inRow = Math.Min(cols, n - r * cols);
                float rowW = inRow * cw + (inRow - 1) * gap;
                float x0 = (ClientSize.Width - rowW) / 2;
                cards.Add(new RectangleF(x0 + c * (cw + gap), y0 + r * (ch + gap), cw, ch));
            }
            RebuildGrid();
        }

        string Label(int no)
        {
            var st = S.StudentOf(no);
            string name = st != null ? (st.Name ?? "").Trim() : "";
            if (S.PickMode != 2 && st != null && !string.IsNullOrWhiteSpace(st.Nick)) return st.Nick.Trim();   // 학생이 정한 닉네임
            if (S.PickMode == 2 || name.Length == 0) return no + "번";
            return name;
        }

        Image LoadPhoto(int no)
        {
            if (S.PickMode == 2) return null;
            var st = S.StudentOf(no);
            return st != null ? Media.LoadImage(st.Photo) : null;
        }

        // 카드 한 장 그리기 (그리드·큰 카드 공용) — 유리 카드
        void DrawCard(Graphics g, RectangleF r, int no, float scale, Image photo)
        {
            Color tint = Border[(no - 1) % Border.Length];
            float rad = Math.Max(8, 16 * k * scale);
            using (var sh = Painter.RoundPath(new RectangleF(r.X + 2, r.Y + 5 * scale, r.Width - 4, r.Height - 2), rad))
            using (var b = new SolidBrush(Color.FromArgb(38, 80, 70, 160))) g.FillPath(b, sh);
            using (var p = Painter.RoundPath(r, rad))
            {
                using (var b = new LinearGradientBrush(r, Color.FromArgb(235, 255, 255, 255), Color.FromArgb(200, 255, 255, 255), 90f)) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(240, 255, 255, 255), Math.Max(1.5f, 2 * scale))) g.DrawPath(pen, p);
            }
            float pad = Math.Max(5, 8 * k * scale);
            var inner = new RectangleF(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2);
            float nameH = inner.Height * 0.27f;
            var ph = new RectangleF(inner.X, inner.Y, inner.Width, inner.Height - nameH);
            var nm = new RectangleF(inner.X, ph.Bottom, inner.Width, nameH);
            using (var pp = Painter.RoundPath(ph, rad * 0.7f))
            {
                if (photo != null)
                {
                    var stt = g.Save();
                    g.SetClip(pp);
                    float sc = Math.Max(ph.Width / photo.Width, ph.Height / photo.Height);
                    g.DrawImage(photo, ph.X + (ph.Width - photo.Width * sc) / 2, ph.Y + (ph.Height - photo.Height * sc) / 2, photo.Width * sc, photo.Height * sc);
                    g.Restore(stt);
                }
                else
                {
                    using (var b = new LinearGradientBrush(ph, ControlPaint.Light(tint, 0.35f), tint, 120f)) g.FillPath(b, pp);
                    var stu = S.StudentOf(no);
                    string shown = stu == null ? "" : !string.IsNullOrWhiteSpace(stu.Nick) ? stu.Nick.Trim() : (stu.Name ?? "").Trim();
                    string big = S.PickMode == 2 || shown.Length == 0 ? no.ToString() : shown.Substring(0, 1);
                    using (var f = PF(ph.Height * 0.42f, true))
                    using (var tb = new SolidBrush(Color.FromArgb(170, Deep(tint))))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(big, f, tb, ph, sf);
                    }
                }
            }
            if (S.PickMode == 0 && S.StudentOf(no) != null && !string.IsNullOrWhiteSpace(S.StudentOf(no).Name))
            {
                float d = Math.Max(16, ph.Height * 0.2f);
                var badge = new RectangleF(ph.X + 5 * scale, ph.Y + 5 * scale, d * 1.25f, d);
                using (var p = Painter.RoundPath(badge, d / 2)) using (var b = new SolidBrush(Color.FromArgb(215, 255, 255, 255))) g.FillPath(b, p);
                using (var f = PF(d * 0.55f, true))
                    TextRenderer.DrawText(g, no.ToString(), f, Rectangle.Round(badge), Deep(tint), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            string label = Label(no);
            float fs = nm.Height * 0.5f;
            using (var f = PF(fs, true))
            {
                var sz = g.MeasureString(label, f);
                Font f2 = f;
                if (sz.Width > nm.Width * 0.92f) f2 = PF(fs * nm.Width * 0.92f / sz.Width, true);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var b = new SolidBrush(Ui.Ink)) g.DrawString(label, f2, b, nm, sf);
                if (f2 != f) f2.Dispose();
            }
        }

        // 뽑힌 카드: 살짝 어둡게 + 그라데이션 테두리 + '뽑기 당첨' 도장
        void DrawStamp(Graphics g, RectangleF r)
        {
            using (var p = Painter.RoundPath(r, 16 * k))
            {
                using (var b = new SolidBrush(Color.FromArgb(120, 70, 60, 120))) g.FillPath(b, p);
                using (var pen = new Pen(new LinearGradientBrush(r, Ui.Accent, Ui.Accent2, 45f), 4 * k)) g.DrawPath(pen, p);
            }
            float d = r.Width * 0.66f;
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height * 0.40f;
            var st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(-10);
            var circ = new RectangleF(-d / 2, -d / 2, d, d);
            using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) g.FillEllipse(b, circ);
            using (var pen = new Pen(new LinearGradientBrush(circ, Ui.Accent, Ui.Accent2, 45f), Math.Max(2, d * 0.05f))) g.DrawEllipse(pen, circ);
            var inner = RectangleF.Inflate(circ, -d * 0.09f, -d * 0.09f);
            using (var pen = new Pen(Color.FromArgb(150, Ui.Accent2), Math.Max(1, d * 0.018f))) g.DrawEllipse(pen, inner);
            // 리본 띠
            float rw = r.Width * 0.92f, rh = d * 0.30f;
            var band = new RectangleF(-rw / 2, -rh / 2, rw, rh);
            using (var bp = Painter.RoundPath(band, rh * 0.25f))
            {
                using (var sh = new SolidBrush(Color.FromArgb(60, 40, 20, 90))) { g.TranslateTransform(0, 2 * k); g.FillPath(sh, bp); g.TranslateTransform(0, -2 * k); }
                using (var b = new LinearGradientBrush(band, Ui.Accent, Ui.Accent2, 0f)) g.FillPath(b, bp);
            }
            using (var f = PF(Math.Max(9, rh * 0.52f), true))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("★ 뽑기 당첨 ★", f, Brushes.White, band, sf);
            }
            g.Restore(st);
        }

        void RebuildGrid()
        {
            if (ClientSize.Width < 50 || ClientSize.Height < 50) return;
            if (grid != null) grid.Dispose();
            grid = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (var g = Graphics.FromImage(grid))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var bg = Ui.Backdrop(ClientSize.Width, ClientSize.Height)) g.DrawImageUnscaled(bg, 0, 0);
                // 위 제목 줄 (유리)
                using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillRectangle(b, 0, 0, ClientSize.Width, TopH);
                using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255))) g.DrawLine(pen, 0, TopH - 1, ClientSize.Width, TopH - 1);
                float bw = TopH * 0.82f;
                closeBtn = new RectangleF(ClientSize.Width - bw - 6 * k, 0, bw, TopH);
                maxBtn = new RectangleF(closeBtn.X - bw, 0, bw, TopH);
                minBtn = new RectangleF(maxBtn.X - bw, 0, bw, TopH);
                // 성별 필터 칩 (명단에 성별이 있을 때만)
                filterR.Clear();
                {
                    string[] fl = { "전체", "남학생", "여학생", gEdit ? "✓ 다 정했어요" : "성별 정하기" };
                    // 칩이 들어갈 자리가 모자라면 칩을 줄임
                    float cs = 1f;
                    using (var f0 = PF(16 * k, true))
                    {
                        float t0 = 0; for (int j = 0; j < 4; j++) t0 += g.MeasureString(fl[j], f0).Width + 34 * k + 8 * k;
                        float room = minBtn.X - 24 * k;
                        cs = Math.Max(0.5f, Math.Min(1f, room / (t0 + 10 * k)));
                    }
                    using (var f = PF(16 * k * cs, true))
                    {
                        float ch = 40 * k * cs, fx = minBtn.X - 12 * k;
                        var ws = new float[4]; float tot = 0;
                        for (int j = 0; j < 4; j++) { ws[j] = g.MeasureString(fl[j], f).Width + 34 * k * cs; tot += ws[j] + 8 * k * cs; }
                        fx -= tot;
                        chipsLeft = fx;
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        for (int j = 0; j < 4; j++)
                        {
                            if (j == 3) fx += 10 * k;
                            var r = new RectangleF(fx, (TopH - ch) / 2, ws[j], ch);
                            bool on = j < 3 ? gFilter == j : gEdit;
                            using (var pth = Painter.RoundPath(r, ch / 2))
                            {
                                if (on)
                                {
                                    Color c1 = j == 1 ? Color.FromArgb(120, 170, 255) : j == 2 ? Color.FromArgb(255, 140, 185) : j == 3 ? Color.FromArgb(120, 200, 160) : Ui.Accent;
                                    Color c2 = j == 1 ? Color.FromArgb(150, 205, 255) : j == 2 ? Color.FromArgb(255, 180, 160) : j == 3 ? Color.FromArgb(150, 220, 200) : Ui.Accent2;
                                    using (var br = new LinearGradientBrush(r, c1, c2, 0f)) g.FillPath(br, pth);
                                }
                                else
                                {
                                    using (var br = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillPath(br, pth);
                                    using (var pen = new Pen(Color.FromArgb(50, 60, 70, 140), 1f)) g.DrawPath(pen, pth);
                                }
                            }
                            using (var br = new SolidBrush(on ? Color.White : Ui.Ink)) g.DrawString(fl[j], f, br, r, sf);
                            filterR.Add(r);
                            fx += ws[j] + 8 * k * cs;
                        }
                    }
                }
                // 제목·안내 글은 칩 왼쪽에 남은 자리만큼만 (넘치면 줄이거나 숨김)
                {
                    float room = chipsLeft - 28 * k - 12 * k;
                    using (var f = PF(22 * k, true))
                    {
                        float tw = g.MeasureString("랜덤 뽑기", f).Width;
                        if (tw <= room)
                        {
                            using (var b = new LinearGradientBrush(new RectangleF(28 * k, 0, 140 * k, TopH), Ui.Accent, Ui.Accent2, 0f)) g.DrawString("랜덤 뽑기", f, b, 28 * k, (TopH - 30 * k) / 2);
                            float ix = 28 * k + tw + 16 * k, iw = chipsLeft - 12 * k - ix;
                            if (iw > 80 * k)
                            {
                                int left = Pool().Count;
                                string info = gEdit ? "카드를 누를 때마다  남 → 여 → 빈칸  으로 바뀌어요" : (gFilter == 1 ? "남학생 · " : gFilter == 2 ? "여학생 · " : "") + (S.PickNoRepeat ? "남은 학생 " + left + "명 · " : "") + "전체 " + S.StudentCount + "명" + (absent.Count > 0 ? " · 제외 " + absent.Count + "명" : "") + "   (카드를 누르면 오늘 제외)";
                                using (var fi = PF(14 * k, false))
                                using (var b = new SolidBrush(Ui.Muted))
                                using (var sfi = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center })
                                    g.DrawString(info, fi, b, new RectangleF(ix, 0, iw, TopH), sfi);
                            }
                        }
                    }
                }
                // 최소화 · 최대화 · 닫기
                if (hotBtn != 0)
                {
                    var hr = hotBtn == 1 ? minBtn : hotBtn == 2 ? maxBtn : closeBtn;
                    var hb = new RectangleF(hr.X + 4 * k, (TopH - hr.Width + 8 * k) / 2, hr.Width - 8 * k, hr.Width - 8 * k);
                    using (var pth = Painter.RoundPath(hb, 8 * k)) using (var br = new SolidBrush(hotBtn == 3 ? Color.FromArgb(255, 226, 226) : Color.FromArgb(225, 230, 250))) g.FillPath(br, pth);
                }
                using (var pen = new Pen(Ui.Ink, 2.2f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float c = 9 * k, cy = TopH / 2;
                    float cx = minBtn.X + minBtn.Width / 2; g.DrawLine(pen, cx - c, cy + c * 0.6f, cx + c, cy + c * 0.6f);
                    cx = maxBtn.X + maxBtn.Width / 2;
                    if (maxed)
                    {
                        g.DrawRectangle(pen, cx - c, cy - c * 0.4f, c * 1.5f, c * 1.5f);
                        g.DrawLines(pen, new[] { new PointF(cx - c * 0.5f, cy - c * 0.4f), new PointF(cx - c * 0.5f, cy - c), new PointF(cx + c, cy - c), new PointF(cx + c, cy + c * 0.5f), new PointF(cx + c * 0.5f, cy + c * 0.5f) });
                    }
                    else g.DrawRectangle(pen, cx - c, cy - c, c * 2, c * 2);
                }
                using (var pen = new Pen(Color.FromArgb(220, 70, 70), 2.5f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float c = 10 * k, cx = closeBtn.X + closeBtn.Width / 2, cy = TopH / 2;
                    g.DrawLine(pen, cx - c, cy - c, cx + c, cy + c); g.DrawLine(pen, cx - c, cy + c, cx + c, cy - c);
                }
                for (int i = 0; i < cards.Count; i++)
                {
                    int no = i + 1;
                    Image photo = null;
                    try { photo = LoadPhoto(no); } catch { }
                    DrawCard(g, cards[i], no, 1f, photo);
                    if (photo != null) photo.Dispose();
                    bool picked = S.PickNoRepeat && S.Picked.Contains(no), off = absent.Contains(no), other = !gEdit && !GenderOk(no);
                    if (off || other)
                    {
                        using (var p = Painter.RoundPath(cards[i], 16 * k)) using (var b = new SolidBrush(Color.FromArgb(190, 246, 247, 255))) g.FillPath(b, p);
                        using (var f = PF(cards[i].Width * 0.15f, true))
                        {
                            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                            using (var b = new SolidBrush(Ui.Muted)) g.DrawString(off ? "제외" : "", f, b, cards[i], sf);
                        }
                    }
                    else if (picked) DrawStamp(g, cards[i]);
                    // 성별 표시
                    var stu = S.StudentOf(no);
                    string gd = stu != null ? stu.Gender : "";
                    if (gd == "남" || gd == "여" || gEdit)
                    {
                        float d = Math.Max(20, cards[i].Width * 0.2f);
                        var cr = new RectangleF(cards[i].Right - d - 6 * k, cards[i].Y + 6 * k, d, d);
                        Color gc = gd == "남" ? Color.FromArgb(110, 160, 250) : gd == "여" ? Color.FromArgb(250, 120, 170) : Color.FromArgb(200, 200, 210);
                        using (var b = new SolidBrush(gc)) g.FillEllipse(b, cr);
                        using (var pen = new Pen(Color.White, 2 * k)) g.DrawEllipse(pen, cr);
                        using (var f = PF(d * 0.5f, true))
                        using (var b = new SolidBrush(Color.White))
                            g.DrawString(gd == "" ? "?" : gd, f, b, cr, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                    }
                }
            }
        }

        // ---------- 뽑기 ----------
        List<int> Pool()
        {
            var l = new List<int>();
            for (int i = 1; i <= S.StudentCount; i++)
                if ((!S.PickNoRepeat || !S.Picked.Contains(i)) && !absent.Contains(i) && GenderOk(i)) l.Add(i);
            return l;
        }

        void StartDraw()
        {
            pool = Pool();
            if (pool.Count == 0) { phase = 0; Invalidate(); return; }
            result = pool[Rnd.Next(pool.Count)];
            hi = pool[Rnd.Next(pool.Count)];
            t0 = DateTime.Now; nextSwitch = t0; lastFrame = t0;
            confetti.Clear();
            if (!S.PickEffect) { Reveal(); return; }
            phase = 1;
            if (S.PickSound) Audio.PlayDrum();
            anim.Interval = 33; anim.Start();
        }

        // 뽑힌 카드 큰 그림: 창 크기에 맞춰 (창을 줄이거나 키우면 다시 그림)
        void MakeBigCard()
        {
            if (result < 1 || ClientSize.Width < 50 || ClientSize.Height < 50) return;
            if (bigCard != null) { bigCard.Dispose(); bigCard = null; }
            float areaH = ClientSize.Height - TopH - BottomH - 70 * k;
            float h = Math.Min(areaH * 0.92f, ClientSize.Width * 0.8f * 1.18f);
            h = Math.Max(60, h); float w = h / 1.18f;
            bigCard = new Bitmap((int)w, (int)h);
            using (var g = Graphics.FromImage(bigCard))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Image photo = null;
                try { photo = LoadPhoto(result); } catch { }
                DrawCard(g, new RectangleF(0, 0, w, h), result, w / Math.Max(1, cards.Count > 0 ? cards[0].Width : w), photo);
                if (photo != null) photo.Dispose();
            }
        }

        void Reveal()
        {
            phase = 2; hi = result; lastPick = result;
            t0 = DateTime.Now;
            if (S.PickNoRepeat && !S.Picked.Contains(result)) { S.Picked.Add(result); if (save != null) save(); }
            if (S.PickSound) Audio.PlayFanfare();
            MakeBigCard();
            RebuildGrid();
            if (S.PickEffect)
            {
                float cx = ClientSize.Width / 2f, cy = ClientSize.Height * 0.45f;
                for (int i = 0; i < 120; i++)
                {
                    double a = Rnd.NextDouble() * Math.PI * 2;
                    float sp = (float)(Rnd.NextDouble() * 16 + 6) * k;
                    confetti.Add(new Piece
                    {
                        X = cx, Y = cy, VX = (float)Math.Cos(a) * sp, VY = (float)Math.Sin(a) * sp - 8 * k,
                        R = (float)(Rnd.NextDouble() * 360), VR = (float)(Rnd.NextDouble() * 20 - 10),
                        W = (float)(Rnd.NextDouble() * 9 + 7) * k, H = (float)(Rnd.NextDouble() * 5 + 4) * k,
                        C = Border[Rnd.Next(Border.Length)]
                    });
                }
            }
            anim.Interval = 33; anim.Start();
        }

        void Back()
        {
            phase = 0; confetti.Clear(); anim.Stop(); Invalidate();
        }

        void Step()
        {
            var now = DateTime.Now;
            float dt = (float)Math.Min(0.06, (now - lastFrame).TotalSeconds) * 60f;
            lastFrame = now;
            if (phase == 1)
            {
                double el = (now - t0).TotalMilliseconds;
                if (el >= Audio.DrumMs) { Reveal(); Invalidate(); return; }
                if (now >= nextSwitch)
                {
                    if (pool.Count > 1) { int n; do { n = pool[Rnd.Next(pool.Count)]; } while (n == hi); hi = n; }
                    double p = el / Audio.DrumMs;
                    nextSwitch = now.AddMilliseconds(60 + 260 * p * p);
                    Invalidate();
                }
                return;
            }
            if (phase == 2)
            {
                for (int i = confetti.Count - 1; i >= 0; i--)
                {
                    var c = confetti[i];
                    c.VY += 0.4f * k * dt; c.VX *= (float)Math.Pow(0.985, dt);
                    c.X += c.VX * dt; c.Y += c.VY * dt; c.R += c.VR * dt;
                    if (c.Y > ClientSize.Height + 20) confetti.RemoveAt(i);
                }
                bool popping = (now - t0).TotalSeconds < 0.6;
                if (!popping && confetti.Count == 0) anim.Stop();
                Invalidate();
            }
        }

        static float EaseOutBack(float x) { const float c1 = 1.70158f, c3 = c1 + 1; return 1 + c3 * (float)Math.Pow(x - 1, 3) + c1 * (float)Math.Pow(x - 1, 2); }

        // ---------- 그리기 ----------
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { Paint2(e.Graphics); } catch { }
        }

        void Paint2(Graphics g)
        {
            if (grid == null) { g.Clear(Color.Black); return; }
            g.DrawImageUnscaled(grid, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (phase == 1 && hi >= 1 && hi <= cards.Count)
            {
                var r = cards[hi - 1];
                r.Inflate(5 * k, 5 * k);
                using (var p = Painter.RoundPath(r, 20 * k))
                {
                    using (var pen = new Pen(Color.FromArgb(70, Ui.Accent2), 16 * k)) g.DrawPath(pen, p);
                    using (var pen = new Pen(new LinearGradientBrush(r, Ui.Accent, Ui.Accent2, 45f), 5 * k)) g.DrawPath(pen, p);
                }
            }

            if (phase == 2 && bigCard != null)
            {
                using (var b = new SolidBrush(Color.FromArgb(175, 240, 236, 255))) g.FillRectangle(b, 0, TopH, ClientSize.Width, ClientSize.Height - TopH - BottomH);
                float t = (float)(DateTime.Now - t0).TotalSeconds;
                float sc = S.PickEffect ? Math.Max(0.05f, EaseOutBack(Math.Min(1f, t / 0.5f))) : 1f;
                float w = bigCard.Width * sc, h = bigCard.Height * sc;
                float cx = ClientSize.Width / 2f, cy = TopH + (ClientSize.Height - TopH - BottomH) / 2f + 14 * k;
                using (var f = PF(26 * k, true))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center };
                    var tr = new RectangleF(0, cy - bigCard.Height / 2f - 48 * k, ClientSize.Width, 40 * k);
                    using (var b = new LinearGradientBrush(new RectangleF(ClientSize.Width / 2f - 80 * k, tr.Y, 160 * k, tr.Height), Ui.Accent, Ui.Accent2, 0f)) g.DrawString("뽑혔어요!", f, b, tr, sf);
                }
                var dst = new RectangleF(cx - w / 2, cy - h / 2, w, h);
                using (var p = Painter.RoundPath(new RectangleF(dst.X - 4 * k, dst.Y - 4 * k, dst.Width + 8 * k, dst.Height + 8 * k), 24 * k))
                {
                    using (var glow = new Pen(Color.FromArgb(60, Ui.Accent2), 26 * k)) g.DrawPath(glow, p);
                    using (var ring = new Pen(new LinearGradientBrush(dst, Ui.Accent, Ui.Accent2, 45f), 4 * k)) g.DrawPath(ring, p);
                }
                g.InterpolationMode = sc < 0.999f ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
                g.DrawImage(bigCard, dst);
                g.SmoothingMode = SmoothingMode.None;
                foreach (var c in confetti)
                {
                    float cs = (float)Math.Cos(c.R * Math.PI / 180), sn = (float)Math.Sin(c.R * Math.PI / 180);
                    float hw = c.W / 2, hh = c.H / 2 * Math.Abs((float)Math.Cos(c.R * Math.PI / 90)) + 1;
                    var pts = new[] {
                        new PointF(c.X - hw * cs + hh * sn, c.Y - hw * sn - hh * cs), new PointF(c.X + hw * cs + hh * sn, c.Y + hw * sn - hh * cs),
                        new PointF(c.X + hw * cs - hh * sn, c.Y + hw * sn + hh * cs), new PointF(c.X - hw * cs - hh * sn, c.Y - hw * sn + hh * cs) };
                    using (var b = new SolidBrush(c.C)) g.FillPolygon(b, pts);
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
            }

            if (phase == 0 && !gEdit && Pool().Count == 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(175, 240, 236, 255))) g.FillRectangle(b, 0, TopH, ClientSize.Width, ClientSize.Height - TopH - BottomH);
                using (var f = PF(32 * k, true))
                using (var tb = new SolidBrush(Ui.Ink))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(gFilter == 0 ? "모든 학생을 뽑았어요\n[초기화하기]를 누르면 다시 뽑을 수 있어요" : (gFilter == 1 ? "남학생" : "여학생") + "을 모두 뽑았어요\n[초기화하기]를 누르거나 [전체]를 눌러 주세요", f, tb, new RectangleF(0, TopH, ClientSize.Width, ClientSize.Height - TopH - BottomH), sf);
                }
            }

            DrawButtons(g);
        }

        float bs = 1f, chipsLeft;   // 아래 버튼 줄 크기 비율
        void DrawButtons(Graphics g)
        {
            btns.Clear();
            var list = new List<PBtn> {
                new PBtn { Id = "back", Text = "←  다시 선택하기" },
                new PBtn { Id = "draw", Text = phase == 1 ? "뽑는 중…" : "뽑기", Primary = true },
                new PBtn { Id = "reset", Text = "↻  초기화하기" } };
            // 창이 좁으면 아래 버튼 줄 전체를 같이 줄임 (겹치지 않게)
            float need;
            using (var f0 = PF(24 * k, true))
            {
                need = 0;
                foreach (var b in list) need += Math.Max(g.MeasureString(b.Text, f0).Width + 70 * k, b.Primary ? 220 * k : 0);
                need += 18 * k * 2 + 2 * (4 * 76 * k + 42 * k + 40 * k);
            }
            bs = Math.Max(0.45f, Math.Min(1f, ClientSize.Width / need));
            float bh = 64 * k * bs, y = ClientSize.Height - BottomH + (BottomH - bh) / 2, gap = 18 * k * bs;
            using (var f = PF(24 * k * bs, true))
            {
                float total = 0;
                foreach (var b in list) { b.R = new RectangleF(0, 0, Math.Max(g.MeasureString(b.Text, f).Width + 70 * k * bs, b.Primary ? 220 * k * bs : 0), bh); total += b.R.Width; }
                total += gap * (list.Count - 1);
                float x = (ClientSize.Width - total) / 2;
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                foreach (var b in list)
                {
                    b.R = new RectangleF(x, y, b.R.Width, bh);
                    using (var p = Painter.RoundPath(b.R, bh / 2))
                    {
                        if (b.Primary)
                        {
                            using (var sh = Painter.RoundPath(new RectangleF(b.R.X + 4 * k, b.R.Y + 6 * k, b.R.Width - 8 * k, b.R.Height), bh / 2)) using (var br = new SolidBrush(Color.FromArgb(70, Ui.Accent))) g.FillPath(br, sh);
                            using (var br = new LinearGradientBrush(b.R, Ui.Accent, Ui.Accent2, 0f)) g.FillPath(br, p);
                            using (var gl = new LinearGradientBrush(b.R, Color.FromArgb(80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f)) g.FillPath(gl, p);
                        }
                        else
                        {
                            using (var br = new SolidBrush(Color.FromArgb(195, 255, 255, 255))) g.FillPath(br, p);
                            using (var pen = new Pen(Color.FromArgb(240, 255, 255, 255), 1.5f * k)) g.DrawPath(pen, p);
                            using (var pen = new Pen(Color.FromArgb(40, 60, 70, 140), 1f)) g.DrawPath(pen, p);
                        }
                    }
                    using (var br = new SolidBrush(b.Primary ? Color.White : Ui.Ink)) g.DrawString(b.Text, f, br, b.R, sf);
                    btns.Add(b);
                    x += b.R.Width + gap;
                }
            }
            // 오른쪽 둥근 버튼: 추첨 효과, 소리
            float d = 76 * k * bs, rx = ClientSize.Width - 24 * k * bs - d;
            var right = new[] { new PBtn { Id = "sound", Text = S.PickSound ? "소리\n켬" : "소리\n끔", Round = true, On = S.PickSound },
                                new PBtn { Id = "effect", Text = "추첨\n효과", Round = true, On = S.PickEffect },
                                new PBtn { Id = "qr", Text = "카드\n꾸미기", Round = true, On = false },
                                new PBtn { Id = "refresh", Text = "새로\n받기", Round = true, On = false } };
            using (var f = PF(15 * k * bs, true))
                foreach (var b in right)
                {
                    b.R = new RectangleF(rx, ClientSize.Height - BottomH + (BottomH - d) / 2, d, d);
                    using (var br = new SolidBrush(Color.FromArgb(195, 255, 255, 255))) g.FillEllipse(br, b.R);
                    using (var pen = new Pen(Color.FromArgb(240, 255, 255, 255), 1.5f * k)) g.DrawEllipse(pen, b.R);
                    if (b.On) using (var pen = new Pen(new LinearGradientBrush(b.R, Ui.Accent, Ui.Accent2, 45f), 3 * k)) g.DrawEllipse(pen, b.R.X + 2 * k, b.R.Y + 2 * k, d - 4 * k, d - 4 * k);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    using (var br = new SolidBrush(b.On ? Ui.Accent : Ui.Muted)) g.DrawString(b.Text, f, br, b.R, sf);
                    if (b.On)
                    {
                        float kk = k * bs;
                        var chk = new RectangleF(b.R.X - 2 * kk, b.R.Y - 2 * kk, 24 * kk, 24 * kk);
                        using (var br = new LinearGradientBrush(chk, Ui.Accent, Ui.Accent2, 45f)) g.FillEllipse(br, chk);
                        using (var pen = new Pen(Color.White, 2.5f * kk))
                            g.DrawLines(pen, new[] { new PointF(chk.X + 6 * kk, chk.Y + 12 * kk), new PointF(chk.X + 10 * kk, chk.Y + 16 * kk), new PointF(chk.X + 18 * kk, chk.Y + 8 * kk) });
                    }
                    btns.Add(b);
                    rx -= d + 14 * k * bs;
                }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            try
            {
                if (closeBtn.Contains(e.Location)) { Close(); return; }
                if (minBtn.Contains(e.Location)) { WindowState = FormWindowState.Minimized; return; }
                if (maxBtn.Contains(e.Location)) { ToggleMax(); return; }
                foreach (var b in btns) if (b.R.Contains(e.Location)) { Act(b.Id); return; }
                for (int j = 0; j < filterR.Count; j++)
                    if (filterR[j].Contains(e.Location))
                    {
                        if (phase != 1)
                        {
                            if (j == 3) gEdit = !gEdit;
                            else { gFilter = j; if (j > 0 && !HasGender()) gEdit = true; }   // 성별이 없으면 바로 정하기
                            if (phase == 2) Back();
                            RebuildGrid(); Invalidate();
                        }
                        return;
                    }
                if (phase == 2) { Back(); return; }
                if (phase == 0)
                    for (int i = 0; i < cards.Count; i++)
                        if (cards[i].Contains(e.Location))
                        {
                            int no = i + 1;
                            if (gEdit)
                            {
                                var su = S.StudentOf(no); string cur = su != null ? su.Gender : "";
                                SetGender(no, cur == "남" ? "여" : cur == "여" ? "" : "남");
                            }
                            else if (!absent.Remove(no)) absent.Add(no);
                            RebuildGrid(); Invalidate(); return;
                        }
            }
            catch { }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // 위 제목 줄을 끌어 창 옮기기
            bool onChip = false; foreach (var r in filterR) if (r.Contains(e.Location)) onChip = true;
            bool onSys = closeBtn.Contains(e.Location) || maxBtn.Contains(e.Location) || minBtn.Contains(e.Location);
            if (e.Y < TopH && !onChip && !onSys && e.Button == MouseButtons.Left)
            {
                if (e.Clicks == 2) { ToggleMax(); return; }
                if (maxed) return;
                ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hb = closeBtn.Contains(e.Location) ? 3 : maxBtn.Contains(e.Location) ? 2 : minBtn.Contains(e.Location) ? 1 : 0;
            if (hb != hotBtn) { hotBtn = hb; Invalidate(new Rectangle((int)minBtn.X, 0, (int)(closeBtn.Right - minBtn.X) + 1, (int)TopH + 1)); }
            bool over = hb != 0;
            foreach (var b in btns) if (b.R.Contains(e.Location)) over = true;
            foreach (var r in filterR) if (r.Contains(e.Location)) over = true;
            if (phase == 0) foreach (var c in cards) if (c.Contains(e.Location)) over = true;
            Cursor = over ? Cursors.Hand : Cursors.Default;
        }

        int hotBtn;
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hotBtn != 0) { hotBtn = 0; Invalidate(); } }

        bool maxed; Rectangle normalBounds;
        void ToggleMax()
        {
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            if (!maxed) { normalBounds = Bounds; maxed = true; Bounds = Screen.FromControl(this).WorkingArea; }
            else { maxed = false; Bounds = normalBounds; }
            Invalidate();
        }

        // 가장자리를 끌어 창 크기 바꾸기
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x84 && (int)m.Result == 1 && WindowState == FormWindowState.Normal && !maxed)
            {
                var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
                int ed = (int)(8 * k);
                bool L = p.X < ed, R = p.X >= ClientSize.Width - ed, T = p.Y < ed, B = p.Y >= ClientSize.Height - ed;
                if (T && L) m.Result = (IntPtr)13; else if (T && R) m.Result = (IntPtr)14;
                else if (B && L) m.Result = (IntPtr)16; else if (B && R) m.Result = (IntPtr)17;
                else if (L) m.Result = (IntPtr)10; else if (R) m.Result = (IntPtr)11;
                else if (T) m.Result = (IntPtr)12; else if (B) m.Result = (IntPtr)15;
            }
        }

        void Act(string id)
        {
            switch (id)
            {
                case "draw": if (phase != 1) { if (gEdit) { gEdit = false; RebuildGrid(); } phase = 0; StartDraw(); } break;
                case "back":
                    // 방금 뽑은 학생을 다시 뽑을 수 있게 되돌리고 고르는 화면으로
                    if (lastPick > 0 && S.Picked.Remove(lastPick) && save != null) save();
                    lastPick = -1; Back(); RebuildGrid(); break;
                case "reset": S.Picked.Clear(); absent.Clear(); lastPick = -1; if (save != null) save(); Back(); RebuildGrid(); break;
                case "effect": S.PickEffect = !S.PickEffect; if (save != null) save(); break;
                case "sound": S.PickSound = !S.PickSound; if (save != null) save(); break;
                case "qr": if (OpenQr != null) OpenQr(); break;
                case "refresh": if (OnRefresh != null) OnRefresh(); break;
            }
            Invalidate();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    }
}
