using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClassroomHud
{
    public static class Media
    {
        // 원본 파일을 설정 폴더(media)로 복사하고 저장된 파일 이름을 돌려준다
        public static string Import(string src)
        {
            try
            {
                Directory.CreateDirectory(Settings.MediaDir);
                string bn = Path.GetFileNameWithoutExtension(src);
                foreach (char c in Path.GetInvalidFileNameChars()) bn = bn.Replace(c, '_');
                if (bn.Length > 40) bn = bn.Substring(0, 40);
                string name = bn + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + Path.GetExtension(src).ToLowerInvariant();
                File.Copy(src, Path.Combine(Settings.MediaDir, name), true);
                return name;
            }
            catch (Exception ex)
            {
                MessageBox.Show("파일을 가져오지 못했어요.\n" + ex.Message, "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        public static string Display(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string bn = Path.GetFileNameWithoutExtension(name);
            int u = bn.LastIndexOf('_');
            if (u > 0 && bn.Length - u == 7) bn = bn.Substring(0, u);
            return bn + Path.GetExtension(name);
        }

        public static Image LoadImage(string name)
        {
            string p = Settings.MediaPath(name);
            if (p == null) return null;
            try { return Image.FromStream(new MemoryStream(File.ReadAllBytes(p))); } catch { return null; }
        }

        public static Font UiFont(float pt, bool bold = false)
        {
            var f = Painter.UiFam;
            var style = bold && f.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
            return new Font(f, pt, style);
        }
    }

    // 소리 고르기: 글자 읽기 / 소리 파일 / 직접 녹음
    public class SoundDialog : GlassForm
    {
        public string Result;
        readonly string tts;
        readonly Label lNow, lRec;
        readonly Button bRec;
        readonly Timer recTimer;
        DateTime recStart;
        bool recording;
        string tempRec;

        public SoundDialog(string title, string current, string ttsText)
        {
            Result = current ?? "";
            tts = ttsText ?? "";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Media.UiFont(10F);
            Text = "소리 고르기 · " + title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 330);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14) };
            lNow = new Label { AutoSize = true, Font = Media.UiFont(10.5F, true), Margin = new Padding(3, 3, 3, 10) };
            t.Controls.Add(lNow);

            t.Controls.Add(new Label { Text = "① 소리 파일 넣기 (mp3, wav, wma, m4a)", AutoSize = true, Font = Media.UiFont(10F, true), Margin = new Padding(3, 6, 3, 2) });
            var bFile = new GButton { Text = "파일 고르기…", AutoSize = true, MinimumSize = new Size(140, 34) };
            bFile.Click += (o, e) =>
            {
                using (var od = new OpenFileDialog { Filter = "소리 파일|*.mp3;*.wav;*.wma;*.m4a|모든 파일|*.*", Title = "소리 파일 고르기" })
                    if (od.ShowDialog(this) == DialogResult.OK) { var n = Media.Import(od.FileName); if (n != null) { Result = n; Refresh2(); } }
            };
            t.Controls.Add(bFile);

            t.Controls.Add(new Label { Text = "② 마이크로 직접 녹음 (최대 60초)", AutoSize = true, Font = Media.UiFont(10F, true), Margin = new Padding(3, 12, 3, 2) });
            var rf = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            bRec = new GButton { Text = "● 녹음 시작", AutoSize = true, MinimumSize = new Size(140, 34), ForeColor = Color.Firebrick };
            lRec = new Label { AutoSize = true, Margin = new Padding(8, 9, 3, 3), ForeColor = Color.DimGray, Text = "버튼을 누르고 말한 뒤 멈춤을 눌러요." };
            bRec.Click += (o, e) => ToggleRec();
            rf.Controls.Add(bRec); rf.Controls.Add(lRec);
            t.Controls.Add(rf);

            var bottom = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
            var bPlay = new GButton { Text = "▶ 들어보기", AutoSize = true, MinimumSize = new Size(110, 34) };
            bPlay.Click += (o, e) => Audio.Announce(tts, Result, 1, false, true);
            var bTts = new GButton { Text = "글자 읽기로 되돌리기", AutoSize = true, MinimumSize = new Size(0, 34) };
            bTts.Click += (o, e) => { Result = ""; Refresh2(); };
            var bOk = new GButton { Text = "확인", AutoSize = true, MinimumSize = new Size(90, 34), DialogResult = DialogResult.OK };
            var bCancel = new GButton { Text = "취소", AutoSize = true, MinimumSize = new Size(90, 34), DialogResult = DialogResult.Cancel };
            bottom.Controls.AddRange(new Control[] { bPlay, bTts, bOk, bCancel });
            t.Controls.Add(bottom);
            Controls.Add(t);
            Ui.Apply(this);
            AcceptButton = bOk; CancelButton = bCancel;

            recTimer = new Timer { Interval = 200 };
            recTimer.Tick += (o, e) =>
            {
                double sec = (DateTime.Now - recStart).TotalSeconds;
                lRec.Text = "녹음 중… " + sec.ToString("0") + "초";
                if (sec >= 60) ToggleRec();
            };
            FormClosing += (o, e) => { Audio.Stop(); if (recording) { recTimer.Stop(); Audio.RecCancel(); } };
            Refresh2();
        }

        void Refresh2()
        {
            lNow.Text = string.IsNullOrEmpty(Result) ? "지금: 안내 문구를 컴퓨터 음성으로 읽어 줘요" : "지금: " + Media.Display(Result);
        }

        void ToggleRec()
        {
            if (!recording)
            {
                Audio.Stop();
                if (!Audio.RecStart())
                {
                    MessageBox.Show(this, "녹음을 시작하지 못했어요. 마이크가 연결되어 있는지 확인해 주세요.", "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                recording = true; recStart = DateTime.Now; recTimer.Start();
                bRec.Text = "■ 멈춤";
            }
            else
            {
                recTimer.Stop(); recording = false;
                bRec.Text = "● 다시 녹음";
                try { Directory.CreateDirectory(Settings.MediaDir); } catch { }
                tempRec = Path.Combine(Settings.MediaDir, "rec_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".wav");
                if (Audio.RecStopSave(tempRec)) { Result = Path.GetFileName(tempRec); lRec.Text = "녹음했어요. [들어보기]로 확인해 보세요."; Refresh2(); }
                else lRec.Text = "녹음을 저장하지 못했어요. 다시 해 주세요.";
            }
        }
    }

    // 이미지 고르기 (PNG, JPG, GIF)
    public class ImageDialog : GlassForm
    {
        public string Result;
        readonly PictureBox pic;
        readonly Label lName;

        public ImageDialog(string title, string current)
        {
            Result = current ?? "";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Media.UiFont(10F);
            Text = "이미지 고르기 · " + title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 420);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = "안내할 때 글자 위에 크게 보여 줘요. 움직이는 GIF도 돼요.", AutoSize = true, ForeColor = Color.DimGray });
            pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.WhiteSmoke };
            t.Controls.Add(pic);
            lName = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 6) };
            t.Controls.Add(lName);
            var bottom = new FlowLayoutPanel { AutoSize = true };
            var bPick = new GButton { Text = "이미지 고르기…", AutoSize = true, MinimumSize = new Size(130, 34) };
            bPick.Click += (o, e) =>
            {
                using (var od = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.gif;*.bmp|모든 파일|*.*", Title = "이미지 고르기" })
                    if (od.ShowDialog(this) == DialogResult.OK) { var n = Media.Import(od.FileName); if (n != null) { Result = n; Show2(); } }
            };
            var bNone = new GButton { Text = "이미지 빼기", AutoSize = true, MinimumSize = new Size(0, 34) };
            bNone.Click += (o, e) => { Result = ""; Show2(); };
            var bOk = new GButton { Text = "확인", AutoSize = true, MinimumSize = new Size(90, 34), DialogResult = DialogResult.OK };
            var bCancel = new GButton { Text = "취소", AutoSize = true, MinimumSize = new Size(90, 34), DialogResult = DialogResult.Cancel };
            bottom.Controls.AddRange(new Control[] { bPick, bNone, bOk, bCancel });
            t.Controls.Add(bottom);
            Controls.Add(t);
            Ui.Apply(this);
            AcceptButton = bOk; CancelButton = bCancel;
            FormClosed += (o, e) => { if (pic.Image != null) pic.Image.Dispose(); };
            Show2();
        }

        void Show2()
        {
            var old = pic.Image;
            pic.Image = Media.LoadImage(Result);
            if (old != null) old.Dispose();
            lName.Text = string.IsNullOrEmpty(Result) ? "이미지 없음" : Media.Display(Result);
        }
    }
}

namespace ClassroomHud
{
    // 메모: 시계 아래에 보여 줄 글 + 메모만의 색·배경·글꼴·크기
    public class MemoForm : GlassForm
    {
        public readonly Settings M;              // 고친 값 (복사본)
        public Action<Settings> LivePreview;
        readonly System.Windows.Forms.TextBox tb;
        readonly PaintBox prev;
        readonly Swatch swText, swBg, swLine;
        readonly Toggle tgBg, tgBold, tgLine;
        readonly Slider slSize, slAlpha;
        readonly System.Windows.Forms.ComboBox cbFont;
        bool ready;

        static System.Windows.Forms.Label Cap(string t) { return new System.Windows.Forms.Label { Text = t, AutoSize = true, ForeColor = Ui.Muted, BackColor = System.Drawing.Color.Transparent, Margin = new System.Windows.Forms.Padding(0, 9, 6, 3) }; }
        static System.Windows.Forms.FlowLayoutPanel Row() { return new System.Windows.Forms.FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = System.Drawing.Color.Transparent, Margin = new System.Windows.Forms.Padding(0, 0, 0, 4), Dock = System.Windows.Forms.DockStyle.Fill }; }
        static string Hex(System.Drawing.Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        Swatch ColorSw(string hex, Action<string> set)
        {
            var sw = new Swatch { Value = Painter.ParseColor(hex), Width = 44, Height = 32, Editable = true, Margin = new System.Windows.Forms.Padding(0, 2, 14, 2) };
            sw.Click += (o, e) =>
            {
                System.Drawing.Color c;
                if (ColorPicker.Pick(this, sw.Value, out c)) { sw.Value = c; set(Hex(c)); Changed(); }
            };
            return sw;
        }

        public MemoForm(Settings current)
        {
            M = current.Clone();
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            Font = Media.UiFont(10.5F);
            Text = "메모";
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(600, 680);
            Padding = new System.Windows.Forms.Padding(12);
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); } catch { }

            var card = new GlassPanel { Dock = System.Windows.Forms.DockStyle.Fill, Padding = new System.Windows.Forms.Padding(18, 14, 18, 14), Alpha = 140 };
            var t = new System.Windows.Forms.TableLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, ColumnCount = 1, BackColor = System.Drawing.Color.Transparent };
            for (int i = 0; i < 8; i++) t.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            t.RowStyles[2] = new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 130);
            t.RowStyles[6] = new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100);

            t.Controls.Add(new System.Windows.Forms.Label { Text = "메모", AutoSize = true, Font = Media.UiFont(15F, true), ForeColor = Ui.Ink, BackColor = System.Drawing.Color.Transparent, Margin = new System.Windows.Forms.Padding(0, 0, 0, 2) });
            t.Controls.Add(new System.Windows.Forms.Label { Text = "시계 아래에 보여 줄 글이에요. 시계와 따로 색·배경·글꼴을 정할 수 있어요. 비우면 안 보여요.", AutoSize = true, ForeColor = Ui.Muted, BackColor = System.Drawing.Color.Transparent, MaximumSize = new System.Drawing.Size(550, 0), Margin = new System.Windows.Forms.Padding(0, 0, 0, 8) });
            tb = new System.Windows.Forms.TextBox { Multiline = true, AcceptsReturn = true, Dock = System.Windows.Forms.DockStyle.Fill, ScrollBars = System.Windows.Forms.ScrollBars.Vertical, Text = (M.Memo ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"), Font = Media.UiFont(12F) };
            tb.TextChanged += (o, e) => Changed();
            t.Controls.Add(tb);

            var r1 = Row();
            r1.Controls.Add(Cap("글자 색")); swText = ColorSw(M.MemoColor, v => M.MemoColor = v); r1.Controls.Add(swText);
            tgBg = new Toggle { Text = "메모지 바탕", Checked = M.MemoBgOn, AutoSize = true, Margin = new System.Windows.Forms.Padding(0, 2, 8, 2) };
            tgBg.CheckedChanged += (o, e) => Changed();
            r1.Controls.Add(tgBg);
            swBg = ColorSw(M.MemoBgColor, v => M.MemoBgColor = v); r1.Controls.Add(swBg);
            r1.Controls.Add(Cap("진하기")); slAlpha = new Slider { Minimum = 20, Maximum = 100, Width = 100, Height = 32 }; slAlpha.Value = M.MemoBgOpacity; slAlpha.ValueChanged += (o, e) => Changed(); r1.Controls.Add(slAlpha);
            t.Controls.Add(r1);

            var r2 = Row();
            r2.Controls.Add(Cap("글꼴"));
            cbFont = new System.Windows.Forms.ComboBox { DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList, Width = 210, MaxDropDownItems = 20, Margin = new System.Windows.Forms.Padding(0, 5, 14, 3) };
            cbFont.Items.Add("(날짜·안내 글꼴과 같게)");
            foreach (var bn in BuiltinFonts.Names) cbFont.Items.Add(bn);
            try { using (var ifc = new System.Drawing.Text.InstalledFontCollection()) foreach (var fam in ifc.Families) if (!fam.Name.StartsWith("@")) cbFont.Items.Add(fam.Name); } catch { }
            if (!string.IsNullOrEmpty(M.MemoFont) && !cbFont.Items.Contains(M.MemoFont)) cbFont.Items.Insert(1, M.MemoFont);
            cbFont.SelectedItem = string.IsNullOrEmpty(M.MemoFont) ? cbFont.Items[0] : M.MemoFont;
            if (cbFont.SelectedIndex < 0) cbFont.SelectedIndex = 0;
            cbFont.SelectedIndexChanged += (o, e) => Changed();
            r2.Controls.Add(cbFont);
            r2.Controls.Add(Cap("크기")); slSize = new Slider { Minimum = 12, Maximum = 40, Width = 120, Height = 32 }; slSize.Value = M.MemoSize; slSize.ValueChanged += (o, e) => Changed(); r2.Controls.Add(slSize);
            t.Controls.Add(r2);

            var r3 = Row();
            tgBold = new Toggle { Text = "굵게", Checked = M.MemoBold, AutoSize = true, Margin = new System.Windows.Forms.Padding(0, 2, 18, 2) };
            tgBold.CheckedChanged += (o, e) => Changed();
            tgLine = new Toggle { Text = "글자 테두리", Checked = M.MemoOutline, AutoSize = true, Margin = new System.Windows.Forms.Padding(0, 2, 8, 2) };
            tgLine.CheckedChanged += (o, e) => Changed();
            r3.Controls.Add(tgBold); r3.Controls.Add(tgLine);
            swLine = ColorSw(M.MemoOutlineColor, v => M.MemoOutlineColor = v); r3.Controls.Add(swLine);
            t.Controls.Add(r3);

            prev = new PaintBox { Dock = System.Windows.Forms.DockStyle.Fill, Cursor = System.Windows.Forms.Cursors.Default, Margin = new System.Windows.Forms.Padding(0, 8, 0, 4) };
            prev.Paint += (o, e) =>
            {
                using (var bmp = Painter.MemoPreview(M, prev.Width, prev.Height))
                using (var clip = Ui.Round(new System.Drawing.RectangleF(0, 0, prev.Width - 1, prev.Height - 1), 12))
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.SetClip(clip);
                    e.Graphics.DrawImageUnscaled(bmp, 0, 0);
                }
            };
            t.Controls.Add(prev);

            var bottom = new System.Windows.Forms.FlowLayoutPanel { AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, BackColor = System.Drawing.Color.Transparent, Margin = new System.Windows.Forms.Padding(0, 8, 0, 0) };
            var bOk = new GButton { Text = "저장", AutoSize = true, MinimumSize = new System.Drawing.Size(100, 38), Tag = "primary", DialogResult = System.Windows.Forms.DialogResult.OK };
            var bCancel = new GButton { Text = "취소", AutoSize = true, MinimumSize = new System.Drawing.Size(90, 38), DialogResult = System.Windows.Forms.DialogResult.Cancel };
            var bClear = new GButton { Text = "글 지우기", AutoSize = true, MinimumSize = new System.Drawing.Size(90, 38) };
            bClear.Click += (o, e) => { tb.Text = ""; tb.Focus(); };
            var bReset = new GButton { Text = "기본 모양", AutoSize = true, MinimumSize = new System.Drawing.Size(90, 38) };
            bReset.Click += (o, e) =>
            {
                ready = false;
                M.MemoColor = "#3A2E1F"; M.MemoBgOn = true; M.MemoBgColor = "#FFF3BF"; M.MemoBgOpacity = 92; M.MemoFont = ""; M.MemoSize = 19; M.MemoBold = true; M.MemoOutline = false;
                swText.Value = Painter.ParseColor(M.MemoColor); swBg.Value = Painter.ParseColor(M.MemoBgColor); tgBg.Checked = true; slAlpha.Value = 92; cbFont.SelectedIndex = 0; slSize.Value = 19; tgBold.Checked = true; tgLine.Checked = false;
                ready = true; Changed();
            };
            bottom.Controls.Add(bOk); bottom.Controls.Add(bCancel); bottom.Controls.Add(bClear); bottom.Controls.Add(bReset);
            t.Controls.Add(bottom);
            card.Controls.Add(t);
            Controls.Add(card);
            CancelButton = bCancel;
            Ui.Apply(this);
            ready = true;
        }

        void Changed()
        {
            if (!ready) return;
            M.Memo = tb.Text.Replace("\r\n", "\n").Trim();
            M.MemoBgOn = tgBg.Checked; M.MemoBgOpacity = slAlpha.Value; M.MemoSize = slSize.Value;
            M.MemoBold = tgBold.Checked; M.MemoOutline = tgLine.Checked;
            M.MemoFont = cbFont.SelectedIndex <= 0 ? "" : cbFont.SelectedItem as string;
            prev.Invalidate();
            if (LivePreview != null) try { LivePreview(M); } catch { }
        }
    }
}
