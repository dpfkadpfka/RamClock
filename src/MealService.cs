using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ClassroomHud
{
    // 오늘 급식: 나이스 교육정보 개방 포털(하이클래스·학교 홈페이지와 같은 급식 자료)
    public static class MealService
    {
        const string Hub = "https://open.neis.go.kr/hub/";

        static string Get(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var wc = new WebClient()) { wc.Encoding = Encoding.UTF8; wc.Headers[HttpRequestHeader.UserAgent] = "RamClock"; return wc.DownloadString(url); }
        }

        static List<Dictionary<string, object>> Rows(string json, string key)
        {
            var list = new List<Dictionary<string, object>>();
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = js.Deserialize<Dictionary<string, object>>(json);
            object arr; if (root == null || !root.TryGetValue(key, out arr)) return list;
            foreach (var part in (System.Collections.ArrayList)arr)
            {
                var d = part as Dictionary<string, object>; object rows;
                if (d != null && d.TryGetValue("row", out rows)) foreach (var r in (System.Collections.ArrayList)rows) list.Add((Dictionary<string, object>)r);
            }
            return list;
        }

        public class School { public string Name, Office, Code, Addr; public override string ToString() { return Name + "  (" + Addr + ")"; } }

        public static List<School> FindSchools(string name)
        {
            var list = new List<School>();
            string json = Get(Hub + "schoolInfo?Type=json&pIndex=1&pSize=30&SCHUL_NM=" + Uri.EscapeDataString(name.Trim()));
            foreach (var r in Rows(json, "schoolInfo"))
                list.Add(new School { Name = r["SCHUL_NM"] as string, Office = r["ATPT_OFCDC_SC_CODE"] as string, Code = r["SD_SCHUL_CODE"] as string, Addr = r["ORG_RDNMA"] as string ?? "" });
            return list;
        }

        // 메뉴 이름만 (알레르기 번호·기호 빼고)
        public static List<string> Menu(string office, string code, DateTime day, out string kcal)
        {
            kcal = null;
            var dishes = new List<string>();
            string json = Get(Hub + "mealServiceDietInfo?Type=json&ATPT_OFCDC_SC_CODE=" + office + "&SD_SCHUL_CODE=" + code + "&MLSV_YMD=" + day.ToString("yyyyMMdd"));
            foreach (var r in Rows(json, "mealServiceDietInfo"))
            {
                if ((r["MMEAL_SC_NM"] as string) != "중식" && dishes.Count > 0) continue;
                dishes.Clear();
                foreach (var raw in Regex.Split(r["DDISH_NM"] as string ?? "", @"<br\s*/?>"))
                {
                    string d = Regex.Replace(raw, @"\([\d\.\s]*\)", "");           // (2.5.6)
                    d = Regex.Replace(d, @"\(정\d*\)|\*|#|&amp;", "").Trim();
                    d = Regex.Replace(d, @"[\d\.]+$", "").Trim();                  // 끝에 붙은 1.2.5.
                    if (d.Length > 0) dishes.Add(d);
                }
                kcal = r.ContainsKey("CAL_INFO") ? r["CAL_INFO"] as string : null;
            }
            return dishes;
        }

        // 학교 정하기 (처음 한 번): 이름 넣고 목록에서 고르기
        public static bool PickSchool(IWin32Window owner, Settings S)
        {
            using (var f = new SchoolForm(S.SchoolName))
            {
                if (f.ShowDialog(owner) != DialogResult.OK || f.Result == null) return false;
                S.SchoolName = f.Result.Name; S.SchoolOffice = f.Result.Office; S.SchoolCode = f.Result.Code;
                return true;
            }
        }

        class SchoolForm : GlassForm
        {
            public School Result;
            readonly TextBox tb; readonly ListBox lb; readonly Label msg;
            public SchoolForm(string cur)
            {
                AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
                Font = Media.UiFont(10.5F); Text = "우리 학교 찾기 (오늘 급식)";
                FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen; TopMost = true; ClientSize = new Size(480, 380);
                var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12) };
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                t.Controls.Add(new Label { Text = "학교 이름을 넣고 [찾기]를 누른 뒤 우리 학교를 골라요.", AutoSize = true });
                var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
                tb = new TextBox { Width = 300, Text = cur ?? "" };
                var bFind = new GButton { Text = "찾기", AutoSize = true, MinimumSize = new Size(80, 32) };
                row.Controls.Add(tb); row.Controls.Add(bFind); t.Controls.Add(row);
                lb = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                t.Controls.Add(lb);
                msg = new Label { AutoSize = true, ForeColor = Color.DimGray };
                t.Controls.Add(msg);
                var bottom = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
                var bOk = new GButton { Text = "이 학교로", AutoSize = true, MinimumSize = new Size(100, 34), Tag = "primary" };
                var bCancel = new GButton { Text = "취소", AutoSize = true, MinimumSize = new Size(80, 34), DialogResult = DialogResult.Cancel };
                bottom.Controls.Add(bOk); bottom.Controls.Add(bCancel); t.Controls.Add(bottom);
                Controls.Add(t); Ui.Apply(this);
                bFind.Click += (o, e) => Find();
                tb.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Find(); } };
                lb.DoubleClick += (o, e) => bOk.PerformClick();
                bOk.Click += (o, e) => { Result = lb.SelectedItem as School; if (Result == null) { msg.Text = "목록에서 학교를 골라 주세요."; return; } DialogResult = DialogResult.OK; Close(); };
                CancelButton = bCancel;
                Shown += (o, e) => { tb.Focus(); if (tb.Text.Trim().Length > 0) Find(); };
            }
            void Find()
            {
                string name = tb.Text.Trim(); if (name.Length < 2) { msg.Text = "학교 이름을 두 글자 이상 넣어 주세요."; return; }
                msg.Text = "찾는 중…"; lb.Items.Clear();
                new Thread(() =>
                {
                    List<School> l = null; string err = null;
                    try { l = FindSchools(name); } catch (Exception ex) { err = ex.Message; }
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (err != null) { msg.Text = "찾지 못했어요. 인터넷 연결을 확인해 주세요. (" + err + ")"; return; }
                            foreach (var s in l) lb.Items.Add(s);
                            msg.Text = l.Count == 0 ? "그 이름의 학교가 없어요. '부천북초'처럼 줄여서도 찾아 보세요." : l.Count + "곳을 찾았어요.";
                            if (l.Count == 1) lb.SelectedIndex = 0;
                        }));
                    }
                    catch { }
                }) { IsBackground = true }.Start();
            }
        }
    }
}
