using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 바로가기 추가: 주소를 넣으면 이름은 알아서 채워 준다
    public class LinkDialog : GlassForm
    {
        public QuickLink Result;
        readonly TextBox tUrl, tName;
        bool nameTouched;

        public LinkDialog(QuickLink cur) : this(cur, null) { }

        // playlist=true: 재생목록 저장용 (유튜브 주소 + 이름)
        public LinkDialog(QuickLink cur, string playlistTitle)
        {
            bool pl = playlistTitle != null;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Media.UiFont(10F);
            Text = pl ? playlistTitle : cur == null ? "바로가기 추가" : "바로가기 고치기";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = new Size(470, 250);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12) };
            for (int i = 0; i < 6; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = pl ? "유튜브·유튜브 뮤직 주소 (재생목록이나 영상 주소)" : "사이트 주소 (예: www.youtube.com)", AutoSize = true });
            var urlRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            tUrl = new TextBox { Width = 320, Text = cur != null ? cur.Url : "", Margin = new Padding(3, 3, 6, 3) };
            var bFile = new GButton { Text = "파일·프로그램…", AutoSize = true, MinimumSize = new Size(0, 30) };
            bFile.Click += (o, e) =>
            {
                using (var od = new OpenFileDialog { Title = "자주 여는 파일이나 프로그램 고르기", Filter = "모든 파일|*.*" })
                    if (od.ShowDialog(this) == DialogResult.OK) { tUrl.Text = od.FileName; if (!nameTouched) tName.Text = Path.GetFileNameWithoutExtension(od.FileName); }
            };
            urlRow.Controls.Add(tUrl); if (!pl) urlRow.Controls.Add(bFile); else tUrl.Width = 420;
            t.Controls.Add(urlRow);
            t.Controls.Add(new Label { Text = "이름 (비워 두면 알아서 정해요)", AutoSize = true, Margin = new Padding(3, 12, 3, 3) });
            tName = new TextBox { Width = pl ? 420 : 320, Text = cur != null ? cur.Name : "" };
            nameTouched = cur != null && !string.IsNullOrEmpty(cur.Name);
            tName.KeyPress += (o, e) => nameTouched = true;
            tUrl.TextChanged += (o, e) => { if (!nameTouched && !pl) tName.Text = AutoName(tUrl.Text); };
            t.Controls.Add(tName);
            var bottom = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
            var bOk = new GButton { Text = pl ? "저장" : cur == null ? "추가" : "확인", AutoSize = true, MinimumSize = new Size(90, 34), Tag = "primary" };
            var bCancel = new GButton { Text = "취소", AutoSize = true, MinimumSize = new Size(90, 34), DialogResult = DialogResult.Cancel };
            bOk.Click += (o, e) =>
            {
                string u = tUrl.Text.Trim();
                if (u.Length == 0 || u == "https://") { tUrl.Focus(); return; }
                string n = tName.Text.Trim(); if (n.Length == 0) n = pl ? "내 재생목록" : AutoName(u);
                Result = new QuickLink(n, u);
                DialogResult = DialogResult.OK; Close();
            };
            bottom.Controls.Add(bOk); bottom.Controls.Add(bCancel);
            t.Controls.Add(bottom);
            Controls.Add(t);
            Ui.Apply(this);
            AcceptButton = bOk; CancelButton = bCancel;
            Shown += (o, e) => { tUrl.Focus(); Ui.BringUp(this); };
        }

        // www.i-scream.co.kr → i-scream, youtube.com → youtube
        public static string AutoName(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length == 0) return "";
            try { if (File.Exists(url) || Directory.Exists(url)) return Path.GetFileNameWithoutExtension(url); } catch { }
            string h = LinkIcons.Host(url);
            if (string.IsNullOrEmpty(h)) return url;
            var parts = h.Split('.');
            int i = 0;
            while (i < parts.Length - 1 && (parts[i] == "www" || parts[i] == "m")) i++;
            string n = parts[i];
            if (n == "youtube") return "유튜브";
            if (n == "i-scream") return "아이스크림";
            if (n == "naver") return "네이버";
            if (n == "google") return "구글";
            return n;
        }
    }
}
