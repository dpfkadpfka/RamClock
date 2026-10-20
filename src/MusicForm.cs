using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClassroomHud
{
    // 시계 위에 붙어 다니는 유튜브(뮤직) 미니 플레이어 + 저장한 재생목록
    public class MusicForm : Form
    {
        public const string Home = "https://music.youtube.com";
        readonly Func<Settings> getS;
        readonly Func<Rectangle> getContent;
        readonly Panel head;
        readonly WebView2 web;
        readonly List<HeadBtn> btns = new List<HeadBtn>();
        HeadBtn hot, bFold;
        string title = "유튜브 뮤직";
        bool folded, ready;
        string pending;
        float k = 1f;
        int HeadH { get { return (int)(34 * k); } }
        const int Pad = 5;

        Settings S { get { return getS(); } }

        class HeadBtn { public string Id, Glyph, Tip; public Rectangle R; }

        public MusicForm(Func<Settings> getS, Func<Rectangle> getContent, float dpiK)
        {
            this.getS = getS; this.getContent = getContent; k = Math.Max(1f, dpiK);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "RamClock 음악";
            BackColor = Color.FromArgb(246, 247, 255);
            Padding = new Padding(Pad);
            DoubleBuffered = true;
            MinimumSize = new Size((int)(260 * k), HeadH + 2 * Pad);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            int w = S.MusicW > 0 ? S.MusicW : (int)(420 * k), h = S.MusicH > 0 ? S.MusicH : (int)(320 * k);
            Size = new Size(w, h);

            head = new Panel { Dock = DockStyle.Top, Height = HeadH, BackColor = BackColor, Cursor = Cursors.SizeAll };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(head, true, null);
            foreach (var b in new[] {
                new HeadBtn { Id = "list", Glyph = "☰", Tip = "재생목록" },
                new HeadBtn { Id = "back", Glyph = "‹", Tip = "뒤로" },
                new HeadBtn { Id = "home", Glyph = "⌂", Tip = "유튜브 뮤직 처음 화면" },
                new HeadBtn { Id = "yt", Glyph = "▶", Tip = "유튜브로 바꾸기" },
                new HeadBtn { Id = "save", Glyph = "☆", Tip = "지금 화면을 재생목록에 저장" },
                new HeadBtn { Id = "fold", Glyph = "▴", Tip = "접기 (노래는 계속 나와요)" },
                new HeadBtn { Id = "close", Glyph = "✕", Tip = "끄기" } }) btns.Add(b);
            bFold = btns.Find(b => b.Id == "fold");
            var tip = new ToolTip();
            head.Paint += PaintHead;
            head.Resize += (o, e) => LayoutBtns();
            head.MouseMove += (o, e) =>
            {
                var h2 = btns.Find(b => b.R.Contains(e.Location));
                if (h2 != hot) { hot = h2; head.Cursor = hot != null ? Cursors.Hand : Cursors.SizeAll; tip.SetToolTip(head, hot != null ? hot.Tip : ""); head.Invalidate(); }
            };
            head.MouseLeave += (o, e) => { hot = null; head.Invalidate(); };
            head.MouseDown += HeadDown;
            head.DoubleClick += (o, e) => { if (hot == null) Fold(!folded); };

            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
            Controls.Add(web);
            Controls.Add(head);
            LayoutBtns();
            HandleCreated += (o, e) => { try { int v = 2; DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { } };   // 윈11 둥근 모서리
            ResizeEnd += (o, e) => { if (!folded) { S.MusicW = Width; S.MusicH = Height; S.Save(); } };
            InitWeb();
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; /* 그림자 */ return cp; }
        }

        // ---------- 웹 화면 ----------
        async void InitWeb()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Wv2.DataDir);
                var env = await CoreWebView2Environment.CreateAsync(null, Wv2.DataDir, new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
                await web.EnsureCoreWebView2Async(env);
                var c = web.CoreWebView2;
                c.Settings.IsStatusBarEnabled = false;
                c.Settings.AreDevToolsEnabled = false;
                // 새 창으로 열리는 링크도 이 창 안에서
                c.NewWindowRequested += (o, e) =>
                {
                    e.Handled = true;
                    string h = LinkIcons.Host(e.Uri) ?? "";
                    if (h.Contains("youtube.com") || h.Contains("youtu.be") || h.Contains("google.com")) c.Navigate(e.Uri);
                    else try { System.Diagnostics.Process.Start(e.Uri); } catch { }
                };
                c.DocumentTitleChanged += (o, e) =>
                {
                    string t = c.DocumentTitle ?? "";
                    foreach (var suf in new[] { " - YouTube Music", " - YouTube" }) if (t.EndsWith(suf)) t = t.Substring(0, t.Length - suf.Length);
                    title = t.Length > 0 ? t : "유튜브";
                    head.Invalidate();
                };
                c.SourceChanged += (o, e) => { try { S.MusicLast = c.Source; } catch { } };
                ready = true;
                c.Navigate(pending ?? Fix(S.MusicLast) ?? Home);
                pending = null;
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                MessageBox.Show(this, "미니 플레이어를 열지 못했어요.\n" + ex.Message, "RamClock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
            }
        }

        static string Fix(string u)
        {
            u = (u ?? "").Trim();
            if (u.Length == 0) return null;
            if (!u.Contains("://")) u = "https://" + u;
            return u;
        }

        public void Play(string url)
        {
            url = Fix(url) ?? Home;
            if (folded) Fold(false);
            if (ready) { try { web.CoreWebView2.Navigate(url); } catch (Exception ex) { Program.Log(ex); } }
            else pending = url;
        }

        public string CurrentUrl { get { try { return ready ? web.CoreWebView2.Source : S.MusicLast; } catch { return S.MusicLast; } } }

        // ---------- 머리줄 ----------
        void LayoutBtns()
        {
            int s = HeadH - (int)(6 * k), y = (HeadH - s) / 2, x = head.Width - s - (int)(2 * k);
            for (int i = btns.Count - 1; i >= 1; i--) { btns[i].R = new Rectangle(x, y, s, s); x -= s + (int)(2 * k); }
            btns[0].R = new Rectangle((int)(2 * k), y, (int)(s * 1.0f), s);
            head.Invalidate();
        }

        void PaintHead(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            bFold.Glyph = folded ? "▾" : "▴";
            bFold.Tip = folded ? "펼치기" : "접기 (노래는 계속 나와요)";
            using (var f = new Font("Segoe UI Symbol", 11f, FontStyle.Regular, GraphicsUnit.Point))
            using (var fs = Media.UiFont(9.5f))
            {
                foreach (var b in btns)
                {
                    if (b == hot)
                        using (var br = new SolidBrush(b.Id == "close" ? Color.FromArgb(255, 226, 226) : Ui.AccentSoft))
                        using (var p = Ui.Round(b.R, 6 * k)) g.FillPath(br, p);
                    var col = b.Id == "save" && IsSaved(CurrentUrl) ? Color.FromArgb(240, 170, 20) : (b.Id == "close" ? Color.FromArgb(225, 70, 70) : Ui.Ink);
                    string gl = b.Id == "save" && IsSaved(CurrentUrl) ? "★" : b.Glyph;
                    TextRenderer.DrawText(g, gl, f, b.R, col, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                int tx = btns[0].R.Right + (int)(6 * k), tr = btns[1].R.Left - (int)(6 * k);
                var rt = new Rectangle(tx, 0, Math.Max(0, tr - tx), HeadH);
                TextRenderer.DrawText(g, "♪ " + title, fs, rt, Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        bool IsSaved(string url)
        {
            if (string.IsNullOrEmpty(url) || S.Playlists == null) return false;
            foreach (var p in S.Playlists) if (Same(p.Url, url)) return true;
            return false;
        }

        static string Key(string u)
        {
            u = (u ?? "").Trim().ToLowerInvariant();
            int i = u.IndexOf("list=");
            if (i >= 0) { string l = u.Substring(i + 5); int a = l.IndexOf('&'); return "list:" + (a >= 0 ? l.Substring(0, a) : l); }
            i = u.IndexOf("v=");
            if (i >= 0) { string l = u.Substring(i + 2); int a = l.IndexOf('&'); return "v:" + (a >= 0 ? l.Substring(0, a) : l); }
            return u.Replace("https://", "").Replace("http://", "").Replace("www.", "").TrimEnd('/');
        }
        static bool Same(string a, string b) { return Key(a) == Key(b); }

        void HeadDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var b = btns.Find(x => x.R.Contains(e.Location));
            if (b != null) { HeadClick(b.Id); return; }
            // 머리줄을 끌면 시계에 붙은 자리가 바뀜
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
            var c = getContent();
            if (!c.IsEmpty) { S.MusicDX = Left - c.Left; S.MusicDY = Top - c.Top; S.Save(); }
        }

        void HeadClick(string id)
        {
            switch (id)
            {
                case "list": ShowListMenu(); break;
                case "back": if (ready && web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack(); break;
                case "home": Play(Home); break;
                case "yt": Play((LinkIcons.Host(CurrentUrl) ?? "").StartsWith("music.") ? "https://www.youtube.com" : Home); break;
                case "save": SaveCurrent(); break;
                case "fold": Fold(!folded); break;
                case "close": Close(); break;
            }
        }

        void ShowListMenu()
        {
            var m = new ContextMenuStrip { Font = Media.UiFont(10f), ShowImageMargin = false };
            if (S.Playlists.Count == 0) m.Items.Add(new ToolStripMenuItem("저장한 재생목록이 없어요") { Enabled = false });
            string cur = CurrentUrl;
            foreach (var p in S.Playlists)
            {
                var pp = p;
                var it = new ToolStripMenuItem((Same(p.Url, cur) ? "▶  " : "     ") + p.Name, null, (o, e) => Play(pp.Url));
                if (Same(p.Url, cur)) it.Font = new Font(m.Font, FontStyle.Bold);
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("☆  지금 화면을 재생목록에 저장…", null, (o, e) => SaveCurrent()));
            m.Items.Add(new ToolStripMenuItem("☰  재생목록 관리…", null, (o, e) => OpenManager()));
            m.Show(head, new Point(btns[0].R.Left, HeadH));
        }

        void SaveCurrent()
        {
            string u = CurrentUrl ?? "";
            if (IsSaved(u)) { OpenManager(); return; }
            string nm = title == "유튜브" || title == "유튜브 뮤직" || title == "YouTube Music" ? "" : title;
            using (var d = new LinkDialog(new QuickLink(nm, u), "재생목록에 저장"))
            {
                d.TopMost = true;
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                S.Playlists.Add(d.Result); S.Save();
            }
            head.Invalidate();
        }

        PlaylistForm mgr;
        void OpenManager()
        {
            if (mgr != null && !mgr.IsDisposed) { Ui.BringUp(mgr); return; }
            mgr = new PlaylistForm(getS, Play, () => CurrentUrl, () => title);
            mgr.FormClosed += (o, e) => { mgr = null; head.Invalidate(); };
            mgr.Show(this);
        }

        void Fold(bool f)
        {
            if (f == folded) return;
            folded = f;
            web.Visible = !f;
            if (f) { MinimumSize = new Size(MinimumSize.Width, 0); Height = HeadH + 2 * Pad; }
            else Height = S.MusicH > 0 ? S.MusicH : (int)(320 * k);
            Follow(getContent());
            head.Invalidate();
        }

        // ---------- 시계에 붙어 다니기 ----------
        public void Follow(Rectangle c)
        {
            if (c.IsEmpty || IsDisposed) return;
            var wa = Screen.FromRectangle(c).WorkingArea;
            int x, y;
            if (S.MusicDX == int.MinValue)
            {
                // 처음: 시계 바로 위, 오른쪽 끝을 맞춰서 (위에 자리가 없으면 아래)
                x = c.Right - Width;
                y = c.Top - (folded ? HeadH + 2 * Pad : Height) - (int)(6 * k);
                if (y < wa.Top) y = c.Bottom + (int)(6 * k);
            }
            else { x = c.Left + S.MusicDX; y = c.Top + S.MusicDY; }
            x = Math.Max(wa.Left, Math.Min(wa.Right - Width, x));
            y = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, y));
            if (Left != x || Top != y) Location = new Point(x, y);
        }

        public void SetTop(bool on) { try { TopMost = on; } catch { } }

        // 테두리 근처는 크기 조절
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x84 /*NCHITTEST*/ && (int)m.Result == 1)
            {
                var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
                int e = Pad + 2;
                bool L = p.X < e, R = p.X >= ClientSize.Width - e, T = p.Y < e, B = p.Y >= ClientSize.Height - e;
                if (folded) { T = B = false; }
                if (T && L) m.Result = (IntPtr)13; else if (T && R) m.Result = (IntPtr)14;
                else if (B && L) m.Result = (IntPtr)16; else if (B && R) m.Result = (IntPtr)17;
                else if (L) m.Result = (IntPtr)10; else if (R) m.Result = (IntPtr)11;
                else if (T) m.Result = (IntPtr)12; else if (B) m.Result = (IntPtr)15;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Ui.Line)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { S.MusicLast = CurrentUrl; S.Save(); } catch { }
            try { if (mgr != null && !mgr.IsDisposed) mgr.Close(); } catch { }
            try { web.Dispose(); } catch { }
            base.OnFormClosed(e);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    // 재생목록 관리 창: 이름·주소 고치기, 순서 바꾸기, 지우기, 바로 재생
    public class PlaylistForm : GlassForm
    {
        readonly Func<Settings> getS;
        readonly DataGridView g;

        public PlaylistForm(Func<Settings> getS, Action<string> play, Func<string> curUrl, Func<string> curTitle)
        {
            this.getS = getS;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Media.UiFont(10F);
            Text = "내 재생목록";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 420);
            MinimumSize = new Size(460, 300);
            TopMost = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14, 10, 14, 12) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = "줄을 두 번 누르면 바로 재생돼요. 이름과 주소는 칸을 눌러 고칠 수 있어요.", AutoSize = true, Margin = new Padding(3, 2, 3, 8) });

            g = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false };
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "이름", FillWeight = 34 });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "url", HeaderText = "주소", FillWeight = 52 });
            var cPlay = new DataGridViewButtonColumn { Name = "play", HeaderText = "", Text = "▶", UseColumnTextForButtonValue = true, FillWeight = 7, MinimumWidth = 40 };
            var cDel = new DataGridViewButtonColumn { Name = "del", HeaderText = "", Text = "✕", UseColumnTextForButtonValue = true, FillWeight = 7, MinimumWidth = 40 };
            g.Columns.Add(cPlay); g.Columns.Add(cDel);
            foreach (var p in getS().Playlists) g.Rows.Add(p.Name, p.Url);
            g.CellContentClick += (o, e) =>
            {
                if (e.RowIndex < 0) return;
                g.EndEdit();
                if (e.ColumnIndex == cDel.Index) { g.Rows.RemoveAt(e.RowIndex); Commit(); }
                else if (e.ColumnIndex == cPlay.Index) play(Convert.ToString(g.Rows[e.RowIndex].Cells["url"].Value));
            };
            g.CellDoubleClick += (o, e) => { if (e.RowIndex >= 0 && e.ColumnIndex < 2 && !g.IsCurrentCellInEditMode) play(Convert.ToString(g.Rows[e.RowIndex].Cells["url"].Value)); };
            g.CellEndEdit += (o, e) => Commit();
            t.Controls.Add(g);

            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
            var bAdd = new GButton { Text = "+ 주소로 추가…", AutoSize = true, Tag = "primary" };
            var bCur = new GButton { Text = "☆ 지금 화면 저장", AutoSize = true };
            var bUp = new GButton { Text = "▲", AutoSize = false, Size = new Size(40, 36) };
            var bDn = new GButton { Text = "▼", AutoSize = false, Size = new Size(40, 36) };
            var bClose = new GButton { Text = "닫기", AutoSize = true, MinimumSize = new Size(80, 36) };
            bAdd.Click += (o, e) =>
            {
                using (var d = new LinkDialog(null, "재생목록 추가"))
                    if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) { g.Rows.Add(d.Result.Name, d.Result.Url); Commit(); }
            };
            bCur.Click += (o, e) =>
            {
                string tt = curTitle(); if (tt == "유튜브" || tt == "유튜브 뮤직") tt = "";
                using (var d = new LinkDialog(new QuickLink(tt, curUrl()), "재생목록에 저장"))
                    if (d.ShowDialog(this) == DialogResult.OK && d.Result != null) { g.Rows.Add(d.Result.Name, d.Result.Url); Commit(); }
            };
            bUp.Click += (o, e) => MoveRow(-1);
            bDn.Click += (o, e) => MoveRow(1);
            bClose.Click += (o, e) => Close();
            bar.Controls.AddRange(new Control[] { bAdd, bCur, bUp, bDn, bClose });
            t.Controls.Add(bar);
            Controls.Add(t);
            Ui.Apply(this);
            Shown += (o, e) => Ui.BringUp(this);
        }

        void MoveRow(int d)
        {
            g.EndEdit();
            int r = g.CurrentCell != null ? g.CurrentCell.RowIndex : -1, to = r + d;
            if (r < 0 || to < 0 || to >= g.Rows.Count) return;
            int ci = g.CurrentCell.ColumnIndex;
            var row = g.Rows[r]; g.Rows.RemoveAt(r); g.Rows.Insert(to, row);
            g.CurrentCell = g.Rows[to].Cells[ci];
            Commit();
        }

        void Commit()
        {
            var S = getS();
            var list = new List<QuickLink>();
            foreach (DataGridViewRow r in g.Rows)
            {
                string n = Convert.ToString(r.Cells["name"].Value).Trim(), u = Convert.ToString(r.Cells["url"].Value).Trim();
                if (u.Length == 0) continue;
                list.Add(new QuickLink(n.Length > 0 ? n : "내 재생목록", u));
            }
            S.Playlists = list;
            S.Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { g.EndEdit(); Commit(); base.OnFormClosing(e); }
    }
}
