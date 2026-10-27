using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 마이크로 교실 소리 크기 재기 (윈도우 기본 녹음 장치)
    public class NoiseMeter : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, int dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

        const int N = 4, Samples = 1600;   // 16kHz × 0.1초
        IntPtr h;
        readonly IntPtr[] hdrs = new IntPtr[N], data = new IntPtr[N];
        readonly int hsz = Marshal.SizeOf(typeof(WAVEHDR));
        public string Error;
        public float Level;   // 0~100

        public bool Start()
        {
            var f = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = 16000, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = 32000, cbSize = 0 };
            int r = waveInOpen(out h, -1, ref f, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) { Error = r == 2 ? "마이크를 찾지 못했어요. 마이크를 꽂거나 윈도우 설정 → 개인 정보 → 마이크에서 앱 사용을 켜 주세요." : "마이크를 열지 못했어요 (오류 " + r + ")"; h = IntPtr.Zero; return false; }
            for (int i = 0; i < N; i++)
            {
                data[i] = Marshal.AllocHGlobal(Samples * 2);
                var w = new WAVEHDR { lpData = data[i], dwBufferLength = (uint)(Samples * 2) };
                hdrs[i] = Marshal.AllocHGlobal(hsz);
                Marshal.StructureToPtr(w, hdrs[i], false);
                waveInPrepareHeader(h, hdrs[i], hsz);
                waveInAddBuffer(h, hdrs[i], hsz);
            }
            waveInStart(h);
            return true;
        }

        // 타이머에서 자주 불러 줌: 다 찬 버퍼를 읽고 다시 넣음
        public void Pump()
        {
            if (h == IntPtr.Zero) return;
            for (int i = 0; i < N; i++)
            {
                var w = (WAVEHDR)Marshal.PtrToStructure(hdrs[i], typeof(WAVEHDR));
                if ((w.dwFlags & 1) == 0) continue;   // WHDR_DONE
                int n = (int)w.dwBytesRecorded / 2;
                if (n > 0)
                {
                    var buf = new short[n];
                    Marshal.Copy(w.lpData, buf, 0, n);
                    double sum = 0; foreach (var s in buf) sum += (double)s * s;
                    double rms = Math.Sqrt(sum / n);
                    double db = 20 * Math.Log10(Math.Max(1, rms) / 32768.0);   // -90 ~ 0
                    float lv = (float)Math.Max(0, Math.Min(100, (db + 70) * 100 / 60));   // -70dB=0, -10dB=100
                    Level = lv > Level ? Level * 0.4f + lv * 0.6f : Level * 0.85f + lv * 0.15f;
                }
                w.dwFlags &= ~1u; w.dwBytesRecorded = 0;
                Marshal.StructureToPtr(w, hdrs[i], false);
                waveInAddBuffer(h, hdrs[i], hsz);
            }
        }

        public void Dispose()
        {
            if (h == IntPtr.Zero) return;
            try { waveInReset(h); } catch { }
            for (int i = 0; i < N; i++) { try { waveInUnprepareHeader(h, hdrs[i], hsz); } catch { } Marshal.FreeHGlobal(hdrs[i]); Marshal.FreeHGlobal(data[i]); }
            try { waveInClose(h); } catch { }
            h = IntPtr.Zero;
        }
    }

    // 소음 신호등 창: 초록(좋아요) · 노랑(조금 시끄러워요) · 빨강(너무 시끄러워요)
    public class NoiseForm : Form
    {
        readonly Settings S;
        readonly Action save;
        readonly NoiseMeter meter = new NoiseMeter();
        readonly Timer t;
        float k;
        DateTime redSince = DateTime.MinValue, lastWarn = DateTime.MinValue;
        int state;   // 0 초록 1 노랑 2 빨강
        Rectangle closeR, sensR;
        bool dragSens;

        public NoiseForm(Settings s, Action saveCb, float dpiK)
        {
            S = s; save = saveCb; k = Math.Max(1f, dpiK);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(28, 32, 48);
            Text = "소음 신호등";
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size((int)(240 * k), (int)(470 * k));
            Location = new Point(wa.Right - Width - (int)(20 * k), wa.Bottom - Height - (int)(20 * k));
            MinimumSize = new Size((int)(140 * k), (int)(280 * k));
            t = new Timer { Interval = 50 };
            t.Tick += (o, e) => Step();
            Shown += (o, e) =>
            {
                if (!meter.Start()) { MessageBox.Show(this, meter.Error, "소음 신호등", MessageBoxButtons.OK, MessageBoxIcon.Warning); Close(); return; }
                t.Start();
            };
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; } }

        void Step()
        {
            meter.Pump();
            float lv = meter.Level;
            int y = S.NoiseYellow, r = Math.Max(S.NoiseYellow + 5, S.NoiseRed);
            int ns = lv >= r ? 2 : lv >= y ? 1 : 0;
            if (ns == 2) { if (redSince == DateTime.MinValue) redSince = DateTime.Now; } else redSince = DateTime.MinValue;
            if (ns != state) { state = ns; }
            // 빨강이 3초 넘게 이어지면 한 번 안내 (30초에 한 번까지)
            if (state == 2 && S.NoiseVoice && (DateTime.Now - redSince).TotalSeconds >= 3 && (DateTime.Now - lastWarn).TotalSeconds >= 30)
            {
                lastWarn = DateTime.Now;
                Audio.Announce("조금만 목소리를 줄여 볼까요?", null, 1, S.Sfx, S.Voice);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = ClientSize.Width, h = ClientSize.Height, pad = 14 * k;
            g.Clear(BackColor);
            // 닫기
            closeR = new Rectangle((int)(w - 34 * k), (int)(8 * k), (int)(26 * k), (int)(26 * k));
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 2.2f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            { float c = 6 * k, cx = closeR.X + closeR.Width / 2f, cy = closeR.Y + closeR.Height / 2f; g.DrawLine(pen, cx - c, cy - c, cx + c, cy + c); g.DrawLine(pen, cx - c, cy + c, cx + c, cy - c); }
            // 신호등 3개
            float top = 40 * k, bottom = h - 96 * k, ch = (bottom - top) / 3f, d = Math.Min(w - pad * 4, ch - 12 * k);
            Color[] on = { Color.FromArgb(46, 204, 113), Color.FromArgb(255, 196, 0), Color.FromArgb(240, 64, 64) };
            int[] order = { 2, 1, 0 };   // 위: 빨강, 가운데: 노랑, 아래: 초록
            for (int i = 0; i < 3; i++)
            {
                int s = order[i];
                var r = new RectangleF((w - d) / 2, top + i * ch + (ch - d) / 2, d, d);
                bool lit = s == state;
                if (lit) using (var glow = new SolidBrush(Color.FromArgb(70, on[s]))) g.FillEllipse(glow, RectangleF.Inflate(r, 8 * k, 8 * k));
                using (var b = new SolidBrush(lit ? on[s] : Color.FromArgb(55, on[s]))) g.FillEllipse(b, r);
            }
            string[] words = { "좋아요", "조금 시끄러워요", "너무 시끄러워요" };
            using (var f = new Font(Painter.UiFam, Math.Max(12, 20 * k * Math.Min(1f, w / (240 * k))), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(on[state]))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(words[state], f, b, new RectangleF(0, bottom, w, 34 * k), sf);
            }
            // 소리 막대 + 민감도(노랑·빨강 기준선): 막대 위를 끌면 기준이 바뀜
            sensR = new Rectangle((int)pad, (int)(h - 52 * k), (int)(w - pad * 2), (int)(22 * k));
            using (var p = Painter.RoundPath(sensR, 11 * k)) using (var b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillPath(b, p);
            float lvW = sensR.Width * meter.Level / 100f;
            if (lvW > 2) using (var p = Painter.RoundPath(new RectangleF(sensR.X, sensR.Y, lvW, sensR.Height), 11 * k)) using (var b = new SolidBrush(on[state])) g.FillPath(b, p);
            foreach (var v in new[] { S.NoiseYellow, S.NoiseRed })
            {
                float x = sensR.X + sensR.Width * v / 100f;
                using (var pen = new Pen(Color.White, 2 * k)) g.DrawLine(pen, x, sensR.Y - 4 * k, x, sensR.Bottom + 4 * k);
            }
            using (var f = new Font(Painter.UiFam, Math.Max(9, 11 * k), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                g.DrawString("막대를 끌어 기준 바꾸기 · 빈 곳을 끌어 옮기기", f, b, new RectangleF(0, h - 26 * k, w, 20 * k), new StringFormat { Alignment = StringAlignment.Center });
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (closeR.Contains(e.Location)) { Close(); return; }
            if (Rectangle.Inflate(sensR, 0, (int)(8 * k)).Contains(e.Location)) { dragSens = true; SetSens(e.X); return; }
            ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragSens) SetSens(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (dragSens) { dragSens = false; if (save != null) save(); } }

        // 끈 자리에 가까운 기준선(노랑/빨강)을 옮김
        void SetSens(int x)
        {
            int v = (int)Math.Round(Math.Max(5, Math.Min(98, (x - sensR.X) * 100f / Math.Max(1, sensR.Width))));
            if (Math.Abs(v - S.NoiseYellow) <= Math.Abs(v - S.NoiseRed)) S.NoiseYellow = Math.Min(v, S.NoiseRed - 5);
            else S.NoiseRed = Math.Max(v, S.NoiseYellow + 5);
            Invalidate();
        }

        // 가장자리 끌어 크기 바꾸기
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x84 && (int)m.Result == 1)
            {
                var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
                int ed = (int)(10 * k);
                if (p.X >= ClientSize.Width - ed && p.Y >= ClientSize.Height - ed) m.Result = (IntPtr)17;
                else if (p.X >= ClientSize.Width - ed) m.Result = (IntPtr)11;
                else if (p.Y >= ClientSize.Height - ed) m.Result = (IntPtr)15;
            }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (var p = Painter.RoundPath(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 26 * k)) Region = new Region(p);
            Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            t.Stop(); meter.Dispose();
            if (save != null) save();
            base.OnFormClosed(e);
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }
}
