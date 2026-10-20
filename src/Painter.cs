using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ClassroomHud
{
    public class Btn
    {
        public string Id;
        public string Text;
        public RectangleF R;
        public bool On;
        public Color? Swatch;
        public Image Icon;
        public bool Round;     // 동그란 아이콘 버튼 (메모 수정·삭제)
        public bool Glyph;     // 글자 대신 아이콘만 (모드·교실 도구)
    }

    public class Grp
    {
        public string Caption;
        public bool NewLine, Plain;
        public string Slot = "extra";   // mode(왼쪽 아래) / tools(오른쪽 아래) / extra(그 아래 가운데)
        public List<Btn> Btns = new List<Btn>();
        public List<List<Btn>> Lines = new List<List<Btn>>();
        public List<float> LineW = new List<float>();
        public float CapW, W, H;
    }

    public class HudState
    {
        public bool Hover;
        public int Popup;                 // 0 없음, 1 글자 색, 2 선생님 안내
        public string BannerLabel, BannerText;
        public DateTime BannerUntil;
        public Image BannerImage;
        public int Frame;
        public long TDur = 300000, TRemain = 300000;
        public bool TRun, TDone;
        public DateTime TEnd;
        public bool WRun;
        public long WBase;
        public DateTime WStart;
        public List<long> Laps = new List<long>();
        public float StickW; public string StickKey;
        public bool Maximized; public int MaxPrevX, MaxPrevY; public float MaxPrevScale;   // 크게 보기
        public bool MusicOpen;                        // 음악 미니 플레이어가 열려 있음
        public bool MemoEdit;                         // 위젯에서 바로 메모를 쓰는 중   // 폭이 매초 바뀌어 흔들리지 않도록

        public long WElapsed(DateTime now) { return WBase + (WRun ? (long)(now - WStart).TotalMilliseconds : 0); }
        public bool BannerOn(DateTime now) { return BannerLabel != null && now < BannerUntil; }
    }

    public class HudTexts { public string Top, AmPm, Digits, Status; }

    public class Composition
    {
        public Bitmap Bmp;
        public Rectangle Win;
        public Rectangle Content;
        public List<Btn> Btns = new List<Btn>();
        public RectangleF Banner = RectangleF.Empty;
        public int MemoY;                                   // 메모가 놓이는 화면 y
        public RectangleF[] Handles = new RectangleF[0];   // 왼위, 오른위, 왼아래, 오른아래
    }

    public static class Painter
    {
        public static readonly string[][] Palette = {
            new[]{"빨강","#FF3B3B"}, new[]{"주황","#FF8A1F"}, new[]{"노랑","#FFC61A"}, new[]{"초록","#1DB863"},
            new[]{"파랑","#1F5FFF"}, new[]{"보라","#8F5CFF"}, new[]{"하양","#FFFFFF"}, new[]{"검정","#111111"}
        };
        static readonly Color Accent = Color.FromArgb(31, 95, 255);
        static readonly Color Ink = Color.FromArgb(20, 33, 61);
        static readonly Color Muted = Color.FromArgb(90, 102, 128);
        static readonly Color Line = Color.FromArgb(201, 210, 227);
        static readonly Color GroupBg = Color.FromArgb(236, 241, 250);

        // ---------- 글꼴 ----------
        static readonly string[] Fallbacks = { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "NanumGothic" };
        static FontFamily Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try { var f = new FontFamily(name); return f; } catch { return null; }
        }
        static FontFamily uiFam;
        public static FontFamily UiFam
        {
            get
            {
                if (uiFam == null) { foreach (var n in Fallbacks) { uiFam = Find(n); if (uiFam != null) break; } if (uiFam == null) uiFam = FontFamily.GenericSansSerif; }
                return uiFam;
            }
        }
        static readonly Dictionary<string, FontFamily> famCache = new Dictionary<string, FontFamily>();
        public static FontFamily HudFam(string name)
        {
            FontFamily f;
            if (name == null) name = "";
            if (famCache.TryGetValue("n:" + name, out f)) return f;
            f = BuiltinFonts.Get(name) ?? Find(name) ?? UiFam;
            famCache["n:" + name] = f;
            return f;
        }
        static readonly List<System.Drawing.Text.PrivateFontCollection> pfcs = new List<System.Drawing.Text.PrivateFontCollection>();
        public static FontFamily FileFam(string mediaName)
        {
            if (string.IsNullOrEmpty(mediaName)) return null;
            FontFamily f;
            if (famCache.TryGetValue("f:" + mediaName, out f)) return f;
            string path = Settings.MediaPath(mediaName);
            if (path == null) return null;
            try
            {
                var c = new System.Drawing.Text.PrivateFontCollection();
                c.AddFontFile(path);
                if (c.Families.Length == 0) return null;
                pfcs.Add(c);
                f = c.Families[0];
                famCache["f:" + mediaName] = f;
                return f;
            }
            catch { return null; }
        }
        public static FontFamily FamFor(Settings S) { return FileFam(S.FontFile) ?? HudFam(S.FontName); }
        public static FontFamily ClockFam(Settings S)
        {
            var f = FileFam(S.ClockFontFile);
            if (f != null) return f;
            if (!string.IsNullOrEmpty(S.ClockFontName)) return HudFam(S.ClockFontName);
            return FamFor(S);
        }

        static int StyleFor(FontFamily f, bool bold)
        {
            if (bold && f.IsStyleAvailable(FontStyle.Bold)) return (int)FontStyle.Bold;
            if (f.IsStyleAvailable(FontStyle.Regular)) return (int)FontStyle.Regular;
            foreach (FontStyle s in new[] { FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic })
                if (f.IsStyleAvailable(s)) return (int)s;
            return 0;
        }

        static Graphics mg;
        static Graphics MG { get { if (mg == null) mg = Graphics.FromImage(new Bitmap(4, 4)); return mg; } }

        public static Color ParseColor(string hex)
        {
            try { return ColorTranslator.FromHtml(hex); } catch { return Accent; }
        }
        public static float Lum(Color c) { return (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f; }
        static string Two(long n) { return n.ToString("00"); }

        public static string FmtTimer(long ms)
        {
            long t = (Math.Max(0, ms) + 999) / 1000, h = t / 3600, m = t % 3600 / 60, s = t % 60;
            return h > 0 ? h + ":" + Two(m) + ":" + Two(s) : Two(m) + ":" + Two(s);
        }
        public static string FmtSw(long ms)
        {
            if (ms < 0) ms = 0;
            long cs = ms / 10 % 100, t = ms / 1000, s = t % 60, m = t / 60 % 60, h = t / 3600;
            return (h > 0 ? h + ":" : "") + Two(m) + ":" + Two(s) + "." + Two(cs);
        }

        public static HudTexts Texts(Settings S, HudState st, DateTime now)
        {
            var t = new HudTexts();
            if (S.Mode == 1)
            {
                t.Top = "타이머";
                t.Digits = FmtTimer(st.TRemain);
                t.Status = st.TDone ? "시간이 끝났어요!" : st.TRun ? "" : (st.TRemain < st.TDur ? "일시정지" : "타이머 준비");
            }
            else if (S.Mode == 2)
            {
                t.Top = "스톱워치";
                long e = st.WElapsed(now);
                t.Digits = FmtSw(e);
                t.Status = st.Laps.Count > 0 ? "랩 " + st.Laps.Count + "   " + FmtSw(st.Laps[st.Laps.Count - 1])
                         : (st.WRun ? "" : (e > 0 ? "일시정지" : "스톱워치 준비"));
            }
            else
            {
                string[] dn = { "일", "월", "화", "수", "목", "금", "토" };
                if (S.ShowDate) t.Top = now.Year + "년 " + Two(now.Month) + "월 " + Two(now.Day) + "일 (" + dn[(int)now.DayOfWeek] + ")";
                t.AmPm = now.Hour < 12 ? "오전" : "오후";
                int h = now.Hour % 12; if (h == 0) h = 12;
                t.Digits = Two(h) + ":" + Two(now.Minute) + (S.ShowSeconds ? ":" + Two(now.Second) : "");
                if (S.ShowStatus) t.Status = Sched.Status(S, now);
            }
            return t;
        }

        public static string Signature(Settings S, HudState st, DateTime now)
        {
            var t = Texts(S, st, now);
            return t.Top + "|" + t.AmPm + "|" + t.Digits + "|" + t.Status + "|" + st.BannerOn(now) + "|" + st.Frame + "|" + st.Hover + "|" + st.Popup + "|" + st.TRun + st.WRun;
        }

        // ---------- 글자·숫자 도형 ----------
        static GraphicsPath TextPath(string text, FontFamily f, float em, bool bold, out float w, out float h)
        {
            var p = new GraphicsPath();
            try { p.AddString(text ?? "", f, StyleFor(f, bold), em, new PointF(0, 0), StringFormat.GenericTypographic); } catch { }
            var b = p.GetBounds();
            if (b.Width <= 0 || b.Height <= 0) { w = 0; h = em; return p; }
            h = Math.Max(em, b.Height);
            using (var m = new Matrix()) { m.Translate(-b.X, -b.Y + (h - b.Height) / 2f); p.Transform(m); }
            w = b.Width;
            return p;
        }

        // 숫자마다 같은 칸 너비·같은 기준선 → 숫자가 바뀌어도 위치가 고정
        static readonly Dictionary<string, float[]> digitMetrics = new Dictionary<string, float[]>();
        static float[] DigitMetrics(FontFamily f, float em, bool bold)
        {
            string key = f.Name + "|" + bold + "|" + em.ToString("0.0");
            float[] m;
            if (digitMetrics.TryGetValue(key, out m)) return m;
            float maxW = 0, minY = float.MaxValue, maxY = float.MinValue;
            for (char c = '0'; c <= '9'; c++)
                using (var p = new GraphicsPath())
                {
                    try { p.AddString(c.ToString(), f, StyleFor(f, bold), em, new PointF(0, 0), StringFormat.GenericTypographic); } catch { }
                    var b = p.GetBounds();
                    if (b.Width <= 0) continue;
                    maxW = Math.Max(maxW, b.Width); minY = Math.Min(minY, b.Y); maxY = Math.Max(maxY, b.Bottom);
                }
            if (maxW <= 0) { maxW = em * 0.55f; minY = 0; maxY = em; }
            m = new[] { maxW, minY, maxY };
            if (digitMetrics.Count > 64) digitMetrics.Clear();
            digitMetrics[key] = m;
            return m;
        }

        static GraphicsPath MonoDigits(string text, FontFamily f, float em, bool bold, out float w, out float h)
        {
            var m = DigitMetrics(f, em, bold);
            float cell = m[0], top = m[1], gap = em * 0.05f, x = 0;
            var res = new GraphicsPath();
            foreach (char c in text ?? "")
            {
                using (var p = new GraphicsPath())
                {
                    try { p.AddString(c.ToString(), f, StyleFor(f, bold), em, new PointF(0, 0), StringFormat.GenericTypographic); } catch { }
                    var b = p.GetBounds();
                    bool dig = char.IsDigit(c);
                    float cw = dig ? cell : (b.Width > 0 ? b.Width + em * 0.08f : em * 0.3f);
                    if (b.Width > 0)
                    {
                        using (var mx = new Matrix()) { mx.Translate(x + (cw - b.Width) / 2f - b.X, -top); p.Transform(mx); }
                        res.AddPath(p, false);
                    }
                    x += cw + gap;
                }
            }
            w = Math.Max(0, x - gap); h = Math.Max(1, m[2] - m[1]);
            return res;
        }

        static void H(GraphicsPath p, float xa, float xb, float yc, float t, float g)
        {
            p.AddPolygon(new[] {
                new PointF(xa+g, yc), new PointF(xa+g+t/2, yc-t/2), new PointF(xb-g-t/2, yc-t/2),
                new PointF(xb-g, yc), new PointF(xb-g-t/2, yc+t/2), new PointF(xa+g+t/2, yc+t/2) });
        }
        static void V(GraphicsPath p, float xc, float ya, float yb, float t, float g)
        {
            p.AddPolygon(new[] {
                new PointF(xc, ya+g), new PointF(xc+t/2, ya+g+t/2), new PointF(xc+t/2, yb-g-t/2),
                new PointF(xc, yb-g), new PointF(xc-t/2, yb-g-t/2), new PointF(xc-t/2, ya+g+t/2) });
        }
        static readonly int[] Mask = { 63, 6, 91, 79, 102, 109, 125, 7, 127, 111 };

        static void Digit(GraphicsPath p, int d, float x, float y, float w, float h, float t, float g)
        {
            int m = Mask[d];
            float xa = x + t / 2, xb = x + w - t / 2, yT = y + t / 2, yM = y + h / 2, yB = y + h - t / 2;
            if ((m & 1) != 0) H(p, xa, xb, yT, t, g);
            if ((m & 2) != 0) V(p, xb, yT, yM, t, g);
            if ((m & 4) != 0) V(p, xb, yM, yB, t, g);
            if ((m & 8) != 0) H(p, xa, xb, yB, t, g);
            if ((m & 16) != 0) V(p, xa, yM, yB, t, g);
            if ((m & 32) != 0) V(p, xa, yT, yM, t, g);
            if ((m & 64) != 0) H(p, xa, xb, yM, t, g);
        }

        static GraphicsPath SegPath(string str, float h, out float width)
        {
            var p = new GraphicsPath();
            float w = h * 0.52f, t = w * 0.2f, g = t * 0.14f, sp = h * 0.13f, x = 0, d = t * 1.05f;
            foreach (char c in str)
            {
                if (c >= '0' && c <= '9') { Digit(p, c - '0', x, 0, w, h, t, g); x += w + sp; }
                else if (c == ':') { p.AddRectangle(new RectangleF(x, h * 0.30f - d / 2, d, d)); p.AddRectangle(new RectangleF(x, h * 0.72f - d / 2, d, d)); x += d + sp; }
                else if (c == '.') { p.AddRectangle(new RectangleF(x, h - d, d, d)); x += d + sp; }
                else x += w * 0.5f;
            }
            width = Math.Max(0, x - sp) + 0.07f * h;
            using (var m = new Matrix(1, 0, -0.07f, 1, 0.07f * h, 0)) p.Transform(m);
            return p;
        }

        public static GraphicsPath RoundPath(RectangleF r, float rad) { return Round(r, rad); }
        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // 글자 꾸미기: 빛나기 → 그림자 → 테두리 → 채우기
        static void Styled(Graphics g, GraphicsPath p, float s, Color fill, Settings S, bool plain, float weight)
        {
            Styled(g, p, s, fill, S, plain, weight, S.Outline, S.OutlineColor, S.OutlineWidth);
        }

        static void Styled(Graphics g, GraphicsPath p, float s, Color fill, Settings S, bool plain, float weight, bool outlineOn, string outlineColor, int outlineWidth)
        {
            if (!plain)
            {
                if (S.Glow)
                {
                    float[] ws = { 16, 10, 5 }; int[] al = { 22, 40, 70 };
                    for (int i = 0; i < ws.Length; i++)
                        using (var pen = new Pen(Color.FromArgb(al[i], fill), ws[i] * s * weight) { LineJoin = LineJoin.Round }) g.DrawPath(pen, p);
                }
                float ow = outlineOn ? outlineWidth * s * weight : 0;
                if (S.Shadow)
                    using (var sh = (GraphicsPath)p.Clone())
                    {
                        using (var m = new Matrix()) { m.Translate(0, 2.4f * s); sh.Transform(m); }
                        using (var p1 = new Pen(Color.FromArgb(24, 0, 0, 0), ow + 6 * s) { LineJoin = LineJoin.Round }) g.DrawPath(p1, sh);
                        using (var p2 = new Pen(Color.FromArgb(42, 0, 0, 0), ow + 2.5f * s) { LineJoin = LineJoin.Round }) g.DrawPath(p2, sh);
                        if (!outlineOn) using (var b2 = new SolidBrush(Color.FromArgb(46, 0, 0, 0))) g.FillPath(b2, sh);
                    }
                if (outlineOn) using (var pen = new Pen(ParseColor(outlineColor), ow) { LineJoin = LineJoin.Round }) g.DrawPath(pen, p);
            }
            using (var b = new SolidBrush(fill)) g.FillPath(b, p);
        }

        // 위젯 바탕(카드)
        static void DrawBackground(Graphics g, RectangleF r, Settings S, float s)
        {
            if (S.BgStyle == 0) return;
            int a = (int)(S.BgOpacity * 2.55f);
            using (var rr = Round(r, S.Radius * s))
            {
                if (S.BgStyle == 3)
                {
                    Image img = null;
                    try { img = BgImg(S.BgImage); } catch { }
                    if (img != null)
                    {
                        var st = g.Save();
                        g.SetClip(rr);
                        float sc = Math.Max(r.Width / img.Width, r.Height / img.Height);
                        var dst = new RectangleF(r.X + (r.Width - img.Width * sc) / 2, r.Y + (r.Height - img.Height * sc) / 2, img.Width * sc, img.Height * sc);
                        using (var ia = new ImageAttributes())
                        {
                            var cm = new ColorMatrix { Matrix33 = S.BgOpacity / 100f };
                            ia.SetColorMatrix(cm);
                            g.DrawImage(img, Rectangle.Round(dst), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                        }
                        g.Restore(st);
                    }
                    else using (var b = new SolidBrush(Color.FromArgb(a, ParseColor(S.BgColor)))) g.FillPath(b, rr);
                }
                else if (S.BgStyle == 2)
                {
                    using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2),
                        Color.FromArgb(a, ParseColor(S.BgColor)), Color.FromArgb(a, ParseColor(S.BgColor2)), 60f)) g.FillPath(b, rr);
                }
                else using (var b = new SolidBrush(Color.FromArgb(a, ParseColor(S.BgColor)))) g.FillPath(b, rr);
                if (S.BgBorder) using (var pen = new Pen(Color.FromArgb(170, ParseColor(S.BgBorderColor)), Math.Max(1.2f, 2f * s))) g.DrawPath(pen, rr);
            }
        }

        static string bgImgName; static Image bgImg;
        static Image BgImg(string name)
        {
            if (name == bgImgName) return bgImg;
            if (bgImg != null) bgImg.Dispose();
            bgImgName = name; bgImg = Media.LoadImage(name);
            return bgImg;
        }

        static List<string> Wrap(string text, FontFamily f, float em, float maxW)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return lines;
            string cur = "";
            foreach (var word in text.Split(' '))
            {
                if (word.Length == 0) continue;
                string cand = cur.Length == 0 ? word : cur + " " + word;
                float w, h;
                using (TextPath(cand, f, em, true, out w, out h)) { }
                if (w > maxW && cur.Length > 0) { lines.Add(cur); cur = word; }
                else cur = cand;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        // 날짜·안내 글자 색: 따로 정하지 않으면 시계 색보다 명도를 살짝 낮춰 구분되게
        public static Color AutoSub(Color c)
        {
            float h, sa, v;
            ColorPicker.ToHsv(c, out h, out sa, out v);
            if (v > 0.3f) v *= 0.80f; else v = Math.Min(1f, v + 0.2f);
            return ColorPicker.FromHsv(h, sa * 0.92f, v);
        }
        public static Color SubOf(Settings S) { return string.IsNullOrEmpty(S.SubColor) ? AutoSub(ParseColor(S.Color)) : ParseColor(S.SubColor); }

        // ---------- 메모 ----------
        public static FontFamily MemoFam(Settings S, FontFamily textFam)
        {
            if (string.IsNullOrEmpty(S.MemoFont)) return textFam;
            return HudFam(S.MemoFont);
        }

        // 메모 한 덩어리(메모지 바탕 + 글자)를 그림으로
        public static Bitmap MemoBitmap(Settings S, FontFamily textFam, float s, float maxW)
        {
            if (string.IsNullOrWhiteSpace(S.Memo)) return null;
            var mf = MemoFam(S, textFam);
            float em = Math.Max(10, S.MemoSize) * s, lineH = em * 1.32f;
            var paths = new List<GraphicsPath>(); var ws = new List<float>();
            foreach (var raw in S.Memo.Replace("\r", "").Split('\n'))
                foreach (var ln in Wrap(raw.Trim(), mf, em, maxW))
                {
                    float w, h;
                    paths.Add(TextPath(ln, mf, em, S.MemoBold, out w, out h)); ws.Add(w);
                }
            if (paths.Count == 0) return null;
            float maxw = 0; foreach (var w in ws) maxw = Math.Max(maxw, w);
            float px = S.MemoBgOn ? 16 * s : 0, py = S.MemoBgOn ? 9 * s : 0, edge = 6 * s;
            float bw = maxw + px * 2, bh = paths.Count * lineH + py * 2;
            var bmp = new Bitmap((int)Math.Ceiling(bw + edge * 2), (int)Math.Ceiling(bh + edge * 2), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var box = new RectangleF(edge, edge, bw, bh);
                if (S.MemoBgOn)
                    using (var rr = Round(box, 10 * s))
                    {
                        using (var sh = Round(new RectangleF(box.X, box.Y + 2 * s, box.Width, box.Height), 10 * s)) using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillPath(b, sh);
                        using (var b = new SolidBrush(Color.FromArgb((int)(Math.Max(10, Math.Min(100, S.MemoBgOpacity)) * 2.55f), ParseColor(S.MemoBgColor)))) g.FillPath(b, rr);
                    }
                Color mc = ParseColor(S.MemoColor);
                float y = box.Y + py;
                for (int i = 0; i < paths.Count; i++)
                {
                    using (var m = new Matrix()) { m.Translate(box.X + (box.Width - ws[i]) / 2f, y + (lineH - em) / 2f); paths[i].Transform(m); }
                    if (S.MemoOutline) Styled(g, paths[i], s, mc, S, false, 0.8f, true, S.MemoOutlineColor, 4);
                    else if (!S.MemoBgOn && S.Shadow) Styled(g, paths[i], s, mc, S, false, 0.8f, false, "#FFFFFF", 1);
                    else using (var b = new SolidBrush(mc)) g.FillPath(b, paths[i]);
                    paths[i].Dispose();
                    y += lineH;
                }
            }
            return bmp;
        }

        // 메모 창 미리보기: 밝은 화면과 어두운 화면 위에 같이
        public static Bitmap MemoPreview(Settings S, int w, int h)
        {
            var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h));
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(250, 251, 255), Color.FromArgb(226, 232, 245), 90f)) g.FillRectangle(b, 0, 0, w / 2, h);
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(28, 36, 58), Color.FromArgb(46, 60, 92), 90f)) g.FillRectangle(b, w / 2, 0, w - w / 2, h);
                var tmp = S.Memo;
                if (string.IsNullOrWhiteSpace(S.Memo)) S.Memo = "메모를 적으면 여기 보여요";
                using (var mb = MemoBitmap(S, FamFor(S), 1f, w * 0.85f))
                    if (mb != null)
                    {
                        float sc = Math.Min(1f, Math.Min(w * 0.95f / mb.Width, h * 0.9f / mb.Height));
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(mb, (w - mb.Width * sc) / 2, (h - mb.Height * sc) / 2, mb.Width * sc, mb.Height * sc);
                    }
                S.Memo = tmp;
            }
            return bmp;
        }

        // ---------- 버튼 묶음 ----------
        static Btn B(string id, string text, bool on = false) { return new Btn { Id = id, Text = text, On = on }; }
        static Grp G(string cap, bool newLine, params Btn[] bs) { var g = new Grp { Caption = cap, NewLine = newLine }; g.Btns.AddRange(bs); return g; }

        static List<Grp> Groups(Settings S, HudState st)
        {
            var gs = new List<Grp>();
            var gm = G(null, false, B("m0", "시간표", S.Mode == 0), B("m1", "타이머", S.Mode == 1), B("m2", "스톱워치", S.Mode == 2)); gm.Slot = "mode"; gs.Add(gm);
            foreach (var b0 in gm.Btns) b0.Glyph = true;
            var tools = new List<Btn>();
            foreach (var t in Settings.ToolList)
                if (S.Tools == null || S.Tools.Contains(t[0]))
                    tools.Add(B(t[0], t[1], (t[0] == "msgs" && st.Popup == 2) || (t[0] == "memo" && st.MemoEdit) || (t[0] == "music" && st.MusicOpen)));
            foreach (var b0 in tools) b0.Glyph = true;
            if (tools.Count > 0) { var gt = G(null, false, tools.ToArray()); gt.Slot = "tools"; gs.Add(gt); }

            if (S.Mode == 1)
            {
                var g = G("타이머", true);
                foreach (var p in S.Presets) g.Btns.Add(B("tp:" + p, p + "분"));
                g.Btns.Add(B("tm-", "−1분")); g.Btns.Add(B("tm+", "+1분"));
                g.Btns.Add(B("tstart", st.TRun ? "일시정지" : (st.TRemain < st.TDur && st.TRemain > 0 ? "다시 시작" : "시작"), true));
                g.Btns.Add(B("treset", "리셋"));
                gs.Add(g);
            }
            else if (S.Mode == 2)
            {
                bool started = st.WBase > 0 || st.WRun;
                gs.Add(G("스톱워치", true, B("wstart", st.WRun ? "일시정지" : (started ? "다시 시작" : "시작"), true), B("wlap", "랩"), B("wreset", "리셋")));
            }
            if (st.Popup == 2)
            {
                var g = G("선생님 안내", true);
                for (int i = 0; i < S.Messages.Count; i++) g.Btns.Add(B("msg:" + i, S.Messages[i].Label));
                if (g.Btns.Count == 0) g.Btns.Add(B("settings", "설정에서 안내 버튼 추가"));
                gs.Add(g);
            }
            return gs;
        }

        // ---------- 전체 그리기 ----------
        public static Composition Compose(Settings S, HudState st, DateTime now, float dpiK, int cx, int top, Rectangle work)
        {
            float s = Math.Max(0.2f, S.Scale) * dpiK, k = dpiK;
            var tx = Texts(S, st, now);
            var f = FamFor(S);
            Color tc = ParseColor(S.Color);
            Color sc2 = SubOf(S);
            bool lightText = Lum(tc) > 0.62f;
            bool bold = S.Bold;
            var pc = new List<Color>();
            float pad = (S.BgStyle > 0 ? 18 : 12) * s, gap = 7 * s;

            var paths = new List<GraphicsPath>(); var pw = new List<float>(); var ph = new List<float>(); var sws = new List<float>();
            float w, h;
            if (!string.IsNullOrEmpty(tx.Top)) { paths.Add(TextPath(tx.Top, f, 17 * s, bold, out w, out h)); pw.Add(w); ph.Add(h); sws.Add(0.8f); pc.Add(sc2); }
            {
                float dh = 76 * s, dw;
                GraphicsPath dp;
                var cf2 = ClockFam(S);
                if (S.DigitFont) { float th; dp = MonoDigits(tx.Digits, cf2, 84 * s, S.ClockBold, out dw, out th); dh = th; }
                else dp = SegPath(tx.Digits, dh, out dw);
                var line = new GraphicsPath();
                float x = 0;
                if (tx.AmPm != null)
                {
                    float aw, ah;
                    using (var ap = TextPath(tx.AmPm, S.DigitFont ? cf2 : f, 24 * s, S.DigitFont ? S.ClockBold : bold, out aw, out ah))
                    {
                        using (var m = new Matrix()) { m.Translate(0, dh * 0.56f - ah / 2); ap.Transform(m); }
                        line.AddPath(ap, false);
                    }
                    x = aw + 10 * s;
                }
                using (var m = new Matrix()) { m.Translate(x, 0); dp.Transform(m); }
                line.AddPath(dp, false); dp.Dispose();
                paths.Add(line); pw.Add(x + dw); ph.Add(dh); sws.Add(-1f); pc.Add(tc);
            }
            if (!string.IsNullOrEmpty(tx.Status)) { paths.Add(TextPath(tx.Status, f, 24 * s, bold, out w, out h)); pw.Add(w); ph.Add(h); sws.Add(1f); pc.Add(sc2); }

            // 메모 (시계 아래, 시계와 따로 꾸밈)
            Bitmap memoBmp = null;
            if (!string.IsNullOrWhiteSpace(S.Memo) && !st.MemoEdit)
            {
                float mw0 = 0; foreach (var v in pw) mw0 = Math.Max(mw0, v);
                memoBmp = MemoBitmap(S, f, s, Math.Max(mw0, 320 * s));
            }

            float maxLine = 0; foreach (var v in pw) maxLine = Math.Max(maxLine, v);
            float cw = maxLine + 2 * pad;
            float ch = pad * 2;
            for (int i = 0; i < ph.Count; i++) ch += ph[i] + (i > 0 ? gap : 0);
            if (memoBmp != null) { cw = Math.Max(cw, memoBmp.Width + pad); ch += gap + memoBmp.Height; }

            // 안내 배너
            bool banner = st.BannerOn(now);
            GraphicsPath bLabel = null; var bLines = new List<GraphicsPath>(); var bLineW = new List<float>();
            float bLabelW = 0, bLabelH = 0, bH = 0, bW = 0, bpad = 12 * s, imgW = 0, imgH = 0;
            if (banner)
            {
                float maxTextW = Math.Max(maxLine, 420 * s);
                float tw = 0;
                if (!string.IsNullOrEmpty(st.BannerLabel)) { bLabel = TextPath(st.BannerLabel, f, 34 * s, true, out bLabelW, out bLabelH); tw = bLabelW; }
                foreach (var ln in Wrap(st.BannerText, f, 21 * s, maxTextW))
                {
                    float lw, lh;
                    bLines.Add(TextPath(ln, f, 21 * s, true, out lw, out lh)); bLineW.Add(lw);
                    tw = Math.Max(tw, lw);
                }
                if (st.BannerImage != null)
                {
                    try
                    {
                        float iw = st.BannerImage.Width, ih = st.BannerImage.Height;
                        float sc = Math.Min(Math.Max(maxTextW, 300 * s) / iw, 220 * s / ih);
                        imgW = iw * sc; imgH = ih * sc;
                        tw = Math.Max(tw, imgW);
                    }
                    catch { imgW = imgH = 0; }
                }
                bW = tw + 2 * bpad;
                bH = bpad * 2 + (imgH > 0 ? imgH + 8 * s : 0) + (bLabel != null ? bLabelH : 0) + bLines.Count * (21 * s * 1.35f) + (bLines.Count > 0 && bLabel != null ? 6 * s : 0);
                cw = Math.Max(cw, bW + pad);
                ch += gap * 2 + bH;
            }

            // 버튼 묶음 배치
            List<Grp> groups = null;
            var rows = new List<List<Grp>>(); var rowW = new List<float>(); var rowH = new List<float>();
            float tbW = 0, tbH = 0, bh = 30 * k, gpad = 4 * k, bgap = 4 * k, ggap = 8 * k, rgap = 6 * k, tpad = 8 * k;
            Font bf = null, cf = null;
            if (st.Hover)
            {
                bf = new Font(UiFam, (float)Math.Round(14 * k), FontStyle.Bold, GraphicsUnit.Pixel);
                cf = new Font(UiFam, (float)Math.Round(12 * k), FontStyle.Bold, GraphicsUnit.Pixel);
                float avail = Math.Max(cw, 880 * k) - tpad * 2;
                groups = Groups(S, st);
                foreach (var grp in groups)
                {
                    grp.CapW = grp.Caption != null ? MG.MeasureString(grp.Caption, cf, PointF.Empty, StringFormat.GenericTypographic).Width + 12 * k : 0;
                    float inner = avail - 2 * gpad - grp.CapW;
                    var cur = new List<Btn>(); float cwid = 0;
                    foreach (var b in grp.Btns)
                    {
                        float bw = b.Swatch.HasValue ? bh : b.Glyph ? (float)Math.Round(bh * 1.15f) : MG.MeasureString(b.Text, bf, PointF.Empty, StringFormat.GenericTypographic).Width + 22 * k;
                        b.R = new RectangleF(0, 0, bw, bh);
                        float add = (cur.Count > 0 ? bgap : 0) + bw;
                        if (cur.Count > 0 && cwid + add > inner) { grp.Lines.Add(cur); grp.LineW.Add(cwid); cur = new List<Btn>(); cwid = 0; add = bw; }
                        cur.Add(b); cwid += add;
                    }
                    if (cur.Count > 0) { grp.Lines.Add(cur); grp.LineW.Add(cwid); }
                    float mw = 0; foreach (var v in grp.LineW) mw = Math.Max(mw, v);
                    grp.W = 2 * gpad + grp.CapW + mw;
                    grp.H = 2 * gpad + grp.Lines.Count * bh + (grp.Lines.Count - 1) * bgap;
                }
                List<Grp> row = null; float rw = 0, rh = 0;
                foreach (var grp in groups)
                {
                    if (grp.Slot != "extra") continue;
                    float add = (row != null && row.Count > 0 ? ggap : 0) + grp.W;
                    if (row == null || grp.NewLine || (row.Count > 0 && rw + add > avail))
                    {
                        if (row != null && row.Count > 0) { rows.Add(row); rowW.Add(rw); rowH.Add(rh); }
                        row = new List<Grp>(); rw = 0; rh = 0; add = grp.W;
                    }
                    row.Add(grp); rw += add; rh = Math.Max(rh, grp.H);
                }
                if (row != null && row.Count > 0) { rows.Add(row); rowW.Add(rw); rowH.Add(rh); }
                foreach (var v in rowW) tbW = Math.Max(tbW, v);
                tbW += tpad * 2;
                tbH = tpad * 2; for (int i = 0; i < rowH.Count; i++) tbH += rowH[i] + (i > 0 ? rgap : 0);
            }

            // 같은 설정이면 폭이 줄어들지 않게 → 매초 창이 좌우로 흔들리지 않음
            string sk = s.ToString("0.000") + "|" + S.Mode + "|" + S.ShowSeconds + "|" + S.DigitFont + "|" + S.ClockFontName + S.ClockFontFile + S.ClockBold + "|" + S.FontName + S.FontFile + S.Bold + "|" + S.BgStyle + "|" + (S.Memo ?? "").GetHashCode() + "|" + banner + "|" + now.Date.Ticks;
            if (st.StickKey != sk) { st.StickKey = sk; st.StickW = 0; }
            cw = Math.Max(cw, st.StickW); st.StickW = cw;
            int CW = (int)Math.Ceiling(cw), CH = (int)Math.Ceiling(ch);
            if ((CW & 1) == 1) CW++;
            var content = new Rectangle(cx - CW / 2, top, CW, CH);
            Rectangle tb = Rectangle.Empty, win = content, rMode = Rectangle.Empty, rTools = Rectangle.Empty, rLinks = Rectangle.Empty, rSys = Rectangle.Empty;
            var links = new List<Btn>(); var sysBtns = new List<Btn>();
            if (st.Hover)
            {
                int off = (int)(6 * k), pp = (int)Math.Round(tpad * 0.75f);
                float ib = (float)Math.Round(bh * 1.15f);
                // 오른쪽 위: 설정 · 항상 위(압정) · 최소화 · 최대화 · 닫기 (아이콘)
                foreach (var id in new[] { "settings", "pin", "hide", "max", "quit" })
                    sysBtns.Add(new Btn { Id = id, Text = "", On = (id == "pin" && S.TopMostOn) || (id == "max" && st.Maximized), R = new RectangleF(0, 0, ib, ib) });
                int SW = (int)Math.Ceiling(sysBtns.Count * ib + (sysBtns.Count - 1) * bgap + 2 * pp), SH = (int)Math.Ceiling(ib + 2 * pp);
                // 왼쪽 위: 자주 가는 사이트
                if (S.Links != null)
                {
                    for (int i = 0; i < S.Links.Count; i++)
                    {
                        var L = S.Links[i]; if (string.IsNullOrWhiteSpace(L.Url)) continue;
                        string t = string.IsNullOrWhiteSpace(L.Name) ? L.Url : L.Name;
                        Image ico = null; try { ico = LinkIcons.Get(L.Url); } catch { }
                        links.Add(new Btn { Id = "link:" + i, Text = t, Icon = ico, R = new RectangleF(0, 0, ib, ib) });
                    }
                    links.Add(new Btn { Id = "linkadd", Text = "+", R = new RectangleF(0, 0, ib, ib) });
                }
                int LW = links.Count > 0 ? (int)Math.Ceiling(links.Count * ib + (links.Count - 1) * bgap + 2 * pp) : 0, LH = SH;
                // 아래: 모드(왼쪽) · 교실 도구(오른쪽)
                Grp gMode = null, gTools = null;
                foreach (var gg in groups) { if (gg.Slot == "mode") gMode = gg; else if (gg.Slot == "tools") gTools = gg; }
                int MW = gMode != null ? (int)Math.Ceiling(gMode.W + 2 * pp) : 0, MH = gMode != null ? (int)Math.Ceiling(gMode.H + 2 * pp) : 0;
                int TW2 = gTools != null ? (int)Math.Ceiling(gTools.W + 2 * pp) : 0, TH2 = gTools != null ? (int)Math.Ceiling(gTools.H + 2 * pp) : 0;
                Func<int, int, int[]> pair = (lw2, rw2) =>
                {
                    int gp = (int)(10 * k);
                    if (lw2 + gp + rw2 <= content.Width) return new[] { content.Left, content.Right - rw2 };
                    int st0 = cx - (lw2 + gp + rw2) / 2;
                    return new[] { st0, st0 + lw2 + gp };
                };
                int bottomTop = content.Bottom + off, bottomH = Math.Max(MH, TH2);
                var px = pair(MW, TW2);
                if (MW > 0) rMode = new Rectangle(px[0], bottomTop, MW, MH);
                if (TW2 > 0) rTools = new Rectangle(px[1], bottomTop, TW2, TH2);
                int nextTop = bottomTop + (bottomH > 0 ? bottomH + off : 0);
                // 타이머·스톱워치 조작, 선생님 안내 목록은 그 아래 가운데
                if (rows.Count > 0)
                {
                    int TW = (int)Math.Ceiling(tbW), TH = (int)Math.Ceiling(tbH);
                    tb = new Rectangle(cx - TW / 2, nextTop, TW, TH);
                    nextTop = tb.Bottom + off;
                }
                int topTop = content.Top - off - Math.Max(SH, LH);
                if (topTop < work.Top) topTop = nextTop;   // 위에 자리가 없으면 맨 아래로
                var tx2 = pair(LW, SW);
                if (LW > 0) rLinks = new Rectangle(tx2[0], topTop, LW, LH);
                rSys = new Rectangle(tx2[1], topTop, SW, SH);
                foreach (var r0 in new[] { rMode, rTools, tb, rLinks, rSys }) if (!r0.IsEmpty) win = Rectangle.Union(win, r0);
            }

            var comp = new Composition { Win = win, Content = content };
            var bmp = new Bitmap(Math.Max(1, Math.Min(8000, win.Width)), Math.Max(1, Math.Min(8000, win.Height)), PixelFormat.Format32bppArgb);
            comp.Bmp = bmp;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                var cr = new RectangleF(content.X - win.X, content.Y - win.Y, content.Width, content.Height);
                using (var hit = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(hit, cr);
                DrawBackground(g, cr, S, s);
                using (var rr = Round(cr, Math.Max(10, S.Radius) * s))
                {
                    if (S.BgStyle == 0 && st.Hover)
                    {
                        using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(b, rr);
                        using (var pen = new Pen(Color.FromArgb(120, 110, 120, 140), Math.Max(1f, 1.5f * k)) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, rr);
                    }
                }

                float y = cr.Y + pad;
                for (int i = 0; i < paths.Count; i++)
                {
                    using (var m = new Matrix()) { m.Translate(cr.X + (cr.Width - pw[i]) / 2f, y); paths[i].Transform(m); }
                    if (sws[i] < 0) Styled(g, paths[i], s, pc[i], S, false, 1f, S.ClockOutline, S.ClockOutlineColor, S.ClockOutlineWidth);
                    else Styled(g, paths[i], s, pc[i], S, false, sws[i]);
                    y += ph[i] + gap;
                    paths[i].Dispose();
                }

                comp.MemoY = content.Top + (int)(y - cr.Y);
                if (memoBmp != null)
                {
                    int mx0 = (int)(cr.X + (cr.Width - memoBmp.Width) / 2f), my0 = (int)y;
                    g.DrawImage(memoBmp, mx0, my0, memoBmp.Width, memoBmp.Height);
                    if (st.Hover)
                    {
                        float d = 26 * k, ex = mx0 + memoBmp.Width - 6 * s;
                        var bDel = new Btn { Id = "memodel", Text = "", Round = true, R = new RectangleF(ex - d * 0.75f, my0 - d * 0.15f, d, d) };
                        var bEd = new Btn { Id = "memo", Text = "", Round = true, R = new RectangleF(bDel.R.X - d - 5 * k, bDel.R.Y, d, d) };
                        DrawRoundIcon(g, bEd, k); DrawRoundIcon(g, bDel, k);
                        comp.Btns.Add(bEd); comp.Btns.Add(bDel);
                    }
                    y += memoBmp.Height + gap;
                    memoBmp.Dispose();
                }

                if (banner)
                {
                    y += gap;
                    var br = new RectangleF(cr.X + (cr.Width - bW) / 2f, y, bW, bH);
                    using (var rr = Round(br, 12 * s))
                    {
                        using (var b = new SolidBrush(lightText ? Color.FromArgb(215, 14, 18, 28) : Color.FromArgb(240, 255, 255, 255))) g.FillPath(b, rr);
                        using (var pen = new Pen(Color.FromArgb(200, tc), Math.Max(1.5f, 2.5f * s))) g.DrawPath(pen, rr);
                    }
                    float by = br.Y + bpad;
                    if (imgH > 0)
                    {
                        try { g.DrawImage(st.BannerImage, new RectangleF(br.X + (br.Width - imgW) / 2f, by, imgW, imgH)); } catch { }
                        by += imgH + 8 * s;
                    }
                    if (bLabel != null)
                    {
                        using (var m = new Matrix()) { m.Translate(br.X + (br.Width - bLabelW) / 2f, by); bLabel.Transform(m); }
                        Styled(g, bLabel, s, tc, S, true, 0); bLabel.Dispose();
                        by += bLabelH + 6 * s;
                    }
                    Color textC = lightText ? Color.FromArgb(240, 244, 250) : Ink;
                    for (int i = 0; i < bLines.Count; i++)
                    {
                        using (var m = new Matrix()) { m.Translate(br.X + (br.Width - bLineW[i]) / 2f, by); bLines[i].Transform(m); }
                        Styled(g, bLines[i], s, textC, S, true, 0); bLines[i].Dispose();
                        by += 21 * s * 1.35f;
                    }
                    comp.Banner = br;
                }

                // 모서리 크기 조절 손잡이
                if (st.Hover && !S.Locked)
                {
                    float hs = 11 * k, ins = hs / 2 + 2 * k, hit = 24 * k;
                    var pts = new[] { new PointF(cr.Left + ins, cr.Top + ins), new PointF(cr.Right - ins, cr.Top + ins), new PointF(cr.Left + ins, cr.Bottom - ins), new PointF(cr.Right - ins, cr.Bottom - ins) };
                    comp.Handles = new RectangleF[4];
                    for (int i = 0; i < 4; i++)
                    {
                        var c = new RectangleF(pts[i].X - hs / 2, pts[i].Y - hs / 2, hs, hs);
                        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, c);
                        using (var pen = new Pen(Accent, Math.Max(1.5f, 2 * k))) g.DrawEllipse(pen, c);
                        comp.Handles[i] = new RectangleF(pts[i].X - hit / 2, pts[i].Y - hit / 2, hit, hit);
                    }
                }

                if (st.Hover && groups != null)
                {
                    g.PixelOffsetMode = PixelOffsetMode.None;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    var sfl = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                    float pp2 = (float)Math.Round(tpad * 0.75f);
                    Func<Rectangle, RectangleF> panel = rr0 =>
                    {
                        var pr0 = new RectangleF(rr0.X - win.X + 0.5f, rr0.Y - win.Y + 0.5f, rr0.Width - 1, rr0.Height - 1);
                        using (var rr = Round(pr0, 12 * k))
                        {
                            using (var b = new SolidBrush(Color.FromArgb(246, 255, 255, 255))) g.FillPath(b, rr);
                            using (var pen = new Pen(Color.FromArgb(220, Line), Math.Max(1f, k))) g.DrawPath(pen, rr);
                        }
                        return pr0;
                    };
                    Action<Grp, float, float> drawGroup = (grp, gx0, gy0) =>
                    {
                        var gr = new RectangleF((float)Math.Round(gx0) + 0.5f, (float)Math.Round(gy0) + 0.5f, (float)Math.Round(grp.W), (float)Math.Round(grp.H));
                        if (!grp.Plain) using (var rr = Round(gr, 10 * k)) using (var b = new SolidBrush(GroupBg)) g.FillPath(b, rr);
                        if (grp.Caption != null)
                            using (var cb = new SolidBrush(Muted))
                                g.DrawString(grp.Caption, cf, cb, new RectangleF(gr.X + gpad + 4 * k, gr.Y + gpad, grp.CapW, bh), sfl);
                        float ly = gr.Y + gpad;
                        foreach (var line in grp.Lines)
                        {
                            float bx = gr.X + gpad + grp.CapW;
                            foreach (var b in line)
                            {
                                b.R = new RectangleF((float)Math.Round(bx) + 0.5f, (float)Math.Round(ly) + 0.5f, (float)Math.Round(b.R.Width), (float)Math.Round(bh));
                                DrawBtn(g, b, bf, sf, k, bh);
                                comp.Btns.Add(b);
                                bx += b.R.Width + bgap;
                            }
                            ly += bh + bgap;
                        }
                    };
                    // 아래 왼쪽: 모드 / 아래 오른쪽: 교실 도구
                    foreach (var grp in groups)
                    {
                        Rectangle at = grp.Slot == "mode" ? rMode : grp.Slot == "tools" ? rTools : Rectangle.Empty;
                        if (at.IsEmpty) continue;
                        var pr0 = panel(at);
                        drawGroup(grp, pr0.X + pp2, pr0.Y + pp2);
                    }
                    // 그 아래 가운데: 타이머 조작·선생님 안내 목록
                    if (rows.Count > 0 && !tb.IsEmpty)
                    {
                        var pr = panel(tb);
                        float ry = pr.Y + tpad;
                        for (int r = 0; r < rows.Count; r++)
                        {
                            float gx = pr.X + (pr.Width - rowW[r]) / 2f;
                            foreach (var grp in rows[r]) { drawGroup(grp, gx, ry); gx += grp.W + ggap; }
                            ry += rowH[r] + rgap;
                        }
                    }
                    // 위 왼쪽: 사이트 로고
                    if (!rLinks.IsEmpty)
                    {
                        var pr = panel(rLinks);
                        float lx = pr.X + pp2;
                        foreach (var b in links)
                        {
                            b.R = new RectangleF((float)Math.Round(lx) + 0.5f, (float)Math.Round(pr.Y + pp2) + 0.5f, b.R.Width, b.R.Height);
                            if (b.Id == "linkadd") DrawSysIcon(g, b, k); else DrawLinkBtn(g, b, bf, k, b.R.Height);
                            comp.Btns.Add(b);
                            lx += b.R.Width + bgap;
                        }
                    }
                    // 위 오른쪽: 설정·압정·최소화·최대화·닫기
                    if (!rSys.IsEmpty)
                    {
                        var pr = panel(rSys);
                        float sx2 = pr.X + pp2;
                        foreach (var b in sysBtns)
                        {
                            b.R = new RectangleF((float)Math.Round(sx2) + 0.5f, (float)Math.Round(pr.Y + pp2) + 0.5f, b.R.Width, b.R.Height);
                            DrawSysIcon(g, b, k);
                            comp.Btns.Add(b);
                            sx2 += b.R.Width + bgap;
                        }
                    }
                    sf.Dispose(); sfl.Dispose();
                }
            }
            if (bf != null) bf.Dispose();
            if (cf != null) cf.Dispose();
            return comp;
        }

        // 오른쪽 위 아이콘 버튼들 (선으로 그린 아이콘)
        static void DrawSysIcon(Graphics g, Btn b, float k)
        {
            var r = b.R;
            bool danger = b.Id == "quit";
            using (var rr = Round(r, 8 * k))
            {
                using (var fb = new SolidBrush(b.On ? Accent : Color.White)) g.FillPath(fb, rr);
                using (var pen = new Pen(b.On ? Accent : Line, Math.Max(1f, k))) g.DrawPath(pen, rr);
            }
            Color c = b.On ? Color.White : (danger ? Color.FromArgb(225, 70, 70) : Ink);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, u = r.Width * 0.18f;
            using (var pen = new Pen(c, Math.Max(1.6f, 2f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var br = new SolidBrush(c))
            {
                switch (b.Id)
                {
                    case "quit":
                        g.DrawLine(pen, cx - u, cy - u, cx + u, cy + u); g.DrawLine(pen, cx - u, cy + u, cx + u, cy - u); break;
                    case "hide":
                        g.DrawLine(pen, cx - u * 1.1f, cy + u * 0.6f, cx + u * 1.1f, cy + u * 0.6f); break;
                    case "max":
                        if (b.On)
                        {   // 원래 크기로 (겹친 네모)
                            g.DrawRectangle(pen, cx - u * 1.1f, cy - u * 0.5f, u * 1.6f, u * 1.6f);
                            g.DrawLines(pen, new[] { new PointF(cx - u * 0.5f, cy - u * 0.5f), new PointF(cx - u * 0.5f, cy - u * 1.1f), new PointF(cx + u * 1.1f, cy - u * 1.1f), new PointF(cx + u * 1.1f, cy + u * 0.5f), new PointF(cx + u * 0.5f, cy + u * 0.5f) });
                        }
                        else g.DrawRectangle(pen, cx - u * 1.05f, cy - u * 1.05f, u * 2.1f, u * 2.1f);
                        break;
                    case "pin":
                        {
                            var st0 = g.Save();
                            g.TranslateTransform(cx, cy); g.RotateTransform(45);
                            float hw = u * 0.55f;
                            g.FillRectangle(br, -hw, -u * 1.25f, hw * 2, u * 1.05f);          // 압정 몸통
                            g.FillRectangle(br, -u * 0.95f, -u * 0.3f, u * 1.9f, u * 0.38f);  // 넓은 머리
                            g.DrawLine(pen, 0, u * 0.1f, 0, u * 1.3f);                         // 바늘
                            g.Restore(st0);
                        }
                        break;
                    case "settings":
                        {
                            // 톱니바퀴
                            int n = 8; float ro = u * 1.25f, ri = u * 0.95f;
                            var pts = new List<PointF>();
                            for (int i = 0; i < n * 2; i++)
                            {
                                double a0 = Math.PI * 2 * i / (n * 2), a1 = Math.PI * 2 * (i + 1) / (n * 2);
                                float rad = i % 2 == 0 ? ro : ri;
                                pts.Add(new PointF(cx + (float)Math.Cos(a0 + 0.06) * rad, cy + (float)Math.Sin(a0 + 0.06) * rad));
                                pts.Add(new PointF(cx + (float)Math.Cos(a1 - 0.06) * rad, cy + (float)Math.Sin(a1 - 0.06) * rad));
                            }
                            g.FillPolygon(br, pts.ToArray());
                            using (var hb = new SolidBrush(b.On ? Accent : Color.White)) g.FillEllipse(hb, cx - u * 0.45f, cy - u * 0.45f, u * 0.9f, u * 0.9f);
                        }
                        break;
                    case "linkadd":
                        g.DrawLine(pen, cx - u, cy, cx + u, cy); g.DrawLine(pen, cx, cy - u, cx, cy + u); break;
                }
            }
        }

        // 로고 + 이름 (크롬 북마크처럼)
        static void DrawLinkBtn(Graphics g, Btn b, Font bf, float k, float bh)
        {
            using (var rr = Round(b.R, 10 * k))
            {
                using (var fb = new SolidBrush(Color.White)) g.FillPath(fb, rr);
                using (var pen = new Pen(Line, Math.Max(1f, k))) g.DrawPath(pen, rr);
            }
            float isz = (float)Math.Round(b.R.Height * 0.68f);
            var ir = new RectangleF((float)Math.Round(b.R.X + (b.R.Width - isz) / 2), (float)Math.Round(b.R.Y + (b.R.Height - isz) / 2), isz, isz);
            if (b.Icon != null)
            {
                var im = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                try { g.DrawImage(b.Icon, ir); } catch { }
                g.InterpolationMode = im;
            }
            else
            {
                using (var cb = new SolidBrush(Color.FromArgb(225, 230, 245))) g.FillEllipse(cb, ir);
                using (var f = new Font(UiFam, isz * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(Accent))
                    g.DrawString(b.Text.Length > 0 ? b.Text.Substring(0, 1) : "?", f, tb, ir, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
        }

        // 메모 오른쪽 위의 연필(수정)·X(삭제) 동그라미
        static void DrawRoundIcon(Graphics g, Btn b, float k)
        {
            var r = b.R;
            using (var sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(sh, r.X, r.Y + 1.5f * k, r.Width, r.Height);
            using (var fb = new SolidBrush(b.Id == "memodel" ? Color.FromArgb(255, 98, 98) : Color.White)) g.FillEllipse(fb, r);
            using (var pen = new Pen(b.Id == "memodel" ? Color.White : Line, Math.Max(1f, 1.2f * k))) g.DrawEllipse(pen, r);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, d = r.Width * 0.22f;
            if (b.Id == "memodel")
                using (var pen = new Pen(Color.White, 2.4f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                { g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d); g.DrawLine(pen, cx - d, cy + d, cx + d, cy - d); }
            else
            {
                // 연필
                var st = g.Save();
                g.TranslateTransform(cx, cy); g.RotateTransform(-45);
                float L = r.Width * 0.30f, W = r.Width * 0.11f;
                using (var b2 = new SolidBrush(Accent)) g.FillRectangle(b2, -L, -W, L * 1.45f, W * 2);
                using (var b3 = new SolidBrush(Color.FromArgb(255, 200, 120))) g.FillPolygon(b3, new[] { new PointF(L * 0.45f, -W), new PointF(L * 0.45f, W), new PointF(L * 0.95f, 0) });
                using (var b4 = new SolidBrush(Ink)) g.FillPolygon(b4, new[] { new PointF(L * 0.78f, -W * 0.35f), new PointF(L * 0.78f, W * 0.35f), new PointF(L * 0.95f, 0) });
                g.Restore(st);
            }
        }

        // 모드·교실 도구 아이콘 (24칸 격자에 선으로 그림)
        static void DrawToolIcon(Graphics g, string id, RectangleF r, Color c, float k)
        {
            float isz = r.Height * 0.76f, f = isz / 24f;
            float ox = r.X + (r.Width - isz) / 2f, oy = r.Y + (r.Height - isz) / 2f;
            Func<float, float, PointF> P = (x, y) => new PointF(ox + x * f, oy + y * f);
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(c, Math.Max(1.5f, 2f * f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var br = new SolidBrush(c))
            {
                Action<float, float, float, float> L = (x1, y1, x2, y2) => g.DrawLine(pen, P(x1, y1), P(x2, y2));
                Action<float[]> PL = a => { var pts = new PointF[a.Length / 2]; for (int i = 0; i < pts.Length; i++) pts[i] = P(a[2 * i], a[2 * i + 1]); g.DrawLines(pen, pts); };
                Action<float, float, float, float, float> RR = (x, y, w, h, rad) => { using (var p = Round(new RectangleF(P(x, y), new SizeF(w * f, h * f)), rad * f)) g.DrawPath(pen, p); };
                Action<float, float, float> CI = (x, y, rad) => g.DrawEllipse(pen, ox + (x - rad) * f, oy + (y - rad) * f, rad * 2 * f, rad * 2 * f);
                Action<float, float, float> DOT = (x, y, rad) => g.FillEllipse(br, ox + (x - rad) * f, oy + (y - rad) * f, rad * 2 * f, rad * 2 * f);
                Action<float, float, float, float, float> ARC = (x, y, rad, a0, sw) => g.DrawArc(pen, ox + (x - rad) * f, oy + (y - rad) * f, rad * 2 * f, rad * 2 * f, a0, sw);
                switch (id)
                {
                    case "m0":   // 시간표: 달력 칸
                        RR(3, 4, 18, 17, 2.5f); L(3, 9.5f, 21, 9.5f); L(8, 2.5f, 8, 5.5f); L(16, 2.5f, 16, 5.5f);
                        L(8.5f, 13, 10.5f, 13); L(13.5f, 13, 15.5f, 13); L(8.5f, 17, 10.5f, 17); L(13.5f, 17, 15.5f, 17); break;
                    case "m1":   // 타이머: 모래시계
                        L(6, 2.5f, 18, 2.5f); L(6, 21.5f, 18, 21.5f);
                        PL(new float[] { 7, 2.5f, 7, 6, 9.5f, 10.3f, 12, 12, 14.5f, 10.3f, 17, 6, 17, 2.5f });
                        PL(new float[] { 7, 21.5f, 7, 18, 9.5f, 13.7f, 12, 12, 14.5f, 13.7f, 17, 18, 17, 21.5f });
                        L(9.5f, 19.5f, 14.5f, 19.5f); break;
                    case "m2":   // 스톱워치
                        CI(12, 13.5f, 7.5f); L(10, 2.5f, 14, 2.5f); L(12, 2.5f, 12, 6); L(12, 13.5f, 15, 10.5f); L(18.5f, 6.5f, 20, 5); break;
                    case "pick": // 뽑기: 주사위
                        RR(3.5f, 3.5f, 17, 17, 3.5f);
                        DOT(8.3f, 8.3f, 1.3f); DOT(15.7f, 8.3f, 1.3f); DOT(12, 12, 1.3f); DOT(8.3f, 15.7f, 1.3f); DOT(15.7f, 15.7f, 1.3f); break;
                    case "memo": // 텍스트: T + 연필
                        PL(new float[] { 4, 5.5f, 4, 4, 15, 4, 15, 5.5f }); L(9.5f, 4, 9.5f, 18); L(7.5f, 18, 11.5f, 18);
                        { var pts = new[] { P(14, 20), P(15, 16.5f), P(20.5f, 11), P(23, 13.5f), P(17.5f, 19) }; g.DrawPolygon(pen, pts); }
                        break;
                    case "music": // 음악: 음표
                        PL(new float[] { 9, 18, 9, 5.5f, 20, 3, 20, 15.5f }); CI(6.5f, 18, 2.5f); CI(17.5f, 15.5f, 2.5f); break;
                    case "msgs": // 선생님 안내: 확성기
                        { var pts = new[] { P(3.5f, 10), P(3.5f, 14), P(6.5f, 14), P(13.5f, 18.5f), P(13.5f, 5.5f), P(6.5f, 10) }; g.DrawPolygon(pen, pts); }
                        ARC(14.35f, 12, 4, -48.6f, 97.2f); ARC(14.4f, 12, 7.5f, -47.2f, 94.4f); break;
                    case "week": // 이번 주: 달력 + 체크
                        RR(3, 4, 18, 17, 2.5f); L(3, 9.5f, 21, 9.5f); L(8, 2.5f, 8, 5.5f); L(16, 2.5f, 16, 5.5f);
                        PL(new float[] { 8.5f, 15, 11, 17.5f, 15.5f, 13 }); break;
                }
            }
            g.SmoothingMode = sm;
        }

        static void DrawBtn(Graphics g, Btn b, Font bf, StringFormat sf, float k, float bh)
        {
            if (b.Swatch.HasValue)
            {
                var cir = new RectangleF(b.R.X + 3 * k, b.R.Y + 3 * k, bh - 6 * k, bh - 6 * k);
                using (var br2 = new SolidBrush(b.Swatch.Value)) g.FillEllipse(br2, cir);
                using (var pen = new Pen(Line, Math.Max(1f, 1.5f * k))) g.DrawEllipse(pen, cir);
                if (b.On) using (var pen = new Pen(Accent, 2.5f * k)) g.DrawEllipse(pen, new RectangleF(b.R.X + 0.5f * k, b.R.Y + 0.5f * k, bh - k, bh - k));
                return;
            }
            using (var rr = Round(b.R, 8 * k))
            {
                using (var fb = new SolidBrush(b.On ? Accent : Color.White)) g.FillPath(fb, rr);
                using (var pen = new Pen(b.On ? Accent : Line, Math.Max(1f, k))) g.DrawPath(pen, rr);
            }
            if (b.Glyph) { DrawToolIcon(g, b.Id, b.R, b.On ? Color.White : Ink, k); return; }
            var tsz = g.MeasureString(b.Text, bf, PointF.Empty, StringFormat.GenericTypographic);
            float tx = (float)Math.Round(b.R.X + (b.R.Width - tsz.Width) / 2f), ty = (float)Math.Round(b.R.Y + (b.R.Height - tsz.Height) / 2f);
            using (var tb2 = new SolidBrush(b.On ? Color.White : Ink)) g.DrawString(b.Text, bf, tb2, tx, ty, StringFormat.GenericTypographic);
        }
    }
}
