using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using JumpDrill.Model;
using JumpDrill.Output;

namespace JumpDrill.Gui
{
    /// <summary>カスタムステージを組むときの小さなダイアログ。</summary>
    internal static class StageDialogs
    {
        /// <summary>名前を入れさせる。取り消したら null。</summary>
        /// <param name="validate">入れた名前が使えなければ理由を返す。使えれば null。</param>
        public static string AskName(IWin32Window owner, string title, string initial, Func<string, string> validate)
        {
            using (var form = NewDialog(title, new Size(380, 120)))
            {
                var box = new TextBox { Text = initial ?? "", Dock = DockStyle.Fill };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(16, 2, 16, 2) };
                var cancel = new Button { Text = Lang.T("キャンセル", "Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(16, 2, 16, 2) };

                form.Controls.Add(Layout(box, ok, cancel, null));
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                Theme.Apply(form);

                form.FormClosing += (s, e) =>
                {
                    if (form.DialogResult != DialogResult.OK) return;

                    string error = box.Text.Trim().Length == 0
                        ? Lang.T("名前を入力してください。", "Enter a name.")
                        : validate?.Invoke(box.Text.Trim());
                    if (error == null) return;

                    MessageBox.Show(form, error, "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                };

                box.SelectAll();
                return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }

        /// <summary>
        /// 作ったドリルを選ばせる。選んだ曲名を一覧の並びで返す。取り消したら null。
        /// 同じ譜面が複数の置き場所にあっても1行にまとめる（記録は曲名で引くので、どれでも同じ）。
        /// </summary>
        /// <param name="direction">
        /// 方向（<c>R8bL5a</c>）。指定すればその方向のドリルだけを出す。null なら全部。
        /// </param>
        /// <param name="limit">［追加（BPM 小／大）］で取る本数。行の空きに合わせる。</param>
        public static List<string> PickDrills(IWin32Window owner, List<DrillLibrary.DrillLevel> drills, bool multiple, string direction = null,
            int limit = CustomStage.MaxDrillsPerRow)
        {
            var unique = drills
                .Where(d => direction == null || DrillNaming.DirectionOf(d.SongName) == direction)
                .GroupBy(d => d.SongName, StringComparer.Ordinal)
                .Select(g => new { Name = g.Key, Where = string.Join(", ", g.Select(d => d.Where).Distinct()), Written = g.Max(d => d.Written) })
                .OrderByDescending(d => d.Written)
                .ToList();

            if (unique.Count == 0)
            {
                MessageBox.Show(owner,
                    direction == null
                        ? Lang.T("JumpDrill で作った譜面が見つかりませんでした。先に生成してください。", "No maps made by JumpDrill were found. Generate one first.")
                        : string.Format(CultureInfo.CurrentCulture, Lang.T("{0} のドリルが見つかりませんでした。先に生成してください。", "No {0} drills were found. Generate one first."), direction),
                    "JumpDrill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            string title = Lang.T("JumpDrill — ドリルの選択", "JumpDrill — choose drills");
            if (direction != null) title += string.Format(CultureInfo.CurrentCulture, Lang.T("（{0}）", " ({0})"), direction);

            using (var form = NewDialog(title, new Size(720, 460)))
            {
                form.MaximizeBox = true;
                var tips = new ToolTip();
                form.Disposed += (s, e) => tips.Dispose();
                form.FormBorderStyle = FormBorderStyle.Sizable;

                var filter = new TextBox { Dock = DockStyle.Fill };
                var list = new ListView
                {
                    Dock = DockStyle.Fill,
                    View = View.Details,
                    FullRowSelect = true,
                    MultiSelect = multiple,
                    HideSelection = false,
                };
                // 行頭に方向の絵（メダルの表と同じもの）。名前の字だけでは向きが読めない
                var icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
                int iconCell = (int)Math.Round(8 * form.DeviceDpi / 96.0);
                int pad = (int)Math.Round(3 * form.DeviceDpi / 96.0);
                icons.ImageSize = new Size(4 * iconCell + 1 + pad * 2, 3 * iconCell + 1 + pad * 2);
                form.Disposed += (s, e) => icons.Dispose();
                list.SmallImageList = icons;

                Func<string, string> iconKey = name =>
                {
                    var sequences = DrillNaming.ParseSequences(name);
                    bool tower = DrillNaming.IsTower(name);
                    string key = string.Concat(sequences.Select(q => q.ToString()));
                    if (key.Length > 0 && tower) key += " tower";
                    if (key.Length == 0 || icons.Images.ContainsKey(key)) return key;

                    var bitmap = new Bitmap(icons.ImageSize.Width, icons.ImageSize.Height);
                    using (var g = Graphics.FromImage(bitmap))
                    using (var line = new Pen(Theme.Line))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        MedalBoard.DrawSequenceIcon(g, sequences, pad, pad, iconCell, Math.Max(1.5f, iconCell / 4f), line, tower);
                    }
                    icons.Images.Add(key, bitmap);
                    return key;
                };

                list.Columns.Add(Lang.T("譜面", "Map"), 400);
                list.Columns.Add(Lang.T("場所", "Location"), 150);
                list.Columns.Add(Lang.T("作成日時", "Written"), 110);

                Action fill = () =>
                {
                    string[] words = filter.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    list.BeginUpdate();
                    list.Items.Clear();
                    foreach (var drill in unique)
                    {
                        string shown = DrillNaming.StripId(drill.Name);
                        if (!words.All(w => drill.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;

                        var item = new ListViewItem(shown) { Tag = drill.Name, ImageKey = iconKey(drill.Name) };
                        item.SubItems.Add(drill.Where);
                        item.SubItems.Add(drill.Written == DateTime.MinValue
                            ? "" : drill.Written.ToString("yy/MM/dd HH:mm", CultureInfo.InvariantCulture));
                        list.Items.Add(item);
                    }
                    list.EndUpdate();
                };
                fill();
                filter.TextChanged += (s, e) => fill();

                var ok = new Button { Text = Lang.T("追加", "Add"), DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(16, 2, 16, 2) };
                var cancel = new Button { Text = Lang.T("キャンセル", "Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(16, 2, 16, 2) };
                if (!multiple) ok.Text = Lang.T("選択", "Select");
                list.DoubleClick += (s, e) => { if (list.SelectedItems.Count > 0) form.DialogResult = DialogResult.OK; };

                var search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
                search.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                search.Controls.Add(new Label { Text = Lang.T("絞り込み", "Filter"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 6, 3) }, 0, 0);
                search.Controls.Add(filter, 1, 0);

                // 1行は1方向なので、方向（行の方向、無ければ選んだドリルの方向）が同じものだけを候補にする。
                // その中から BPM の小さい／大きい方から limit 本を取り、遅い順に並べて返す。
                List<string> bySpeed = null;
                Func<string> speedDirection = () => direction ??
                    (list.SelectedItems.Count > 0 ? DrillNaming.DirectionOf((string)list.SelectedItems[0].Tag) : null);
                Action<bool> pickBySpeed = fastest =>
                {
                    string target = speedDirection();
                    if (string.IsNullOrEmpty(target)) return;

                    var chosen = CustomStage.PickBySpeed(list.Items.Cast<ListViewItem>().Select(i => (string)i.Tag), target, fastest, limit);
                    if (chosen.Count == 0) return;

                    bySpeed = chosen;
                    form.DialogResult = DialogResult.OK;
                };

                var extra = new List<Button>();
                if (multiple)
                {
                    var slow = new Button { Text = Lang.T("追加（BPM 小）", "Add (lowest BPM)"), AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
                    var fast = new Button { Text = Lang.T("追加（BPM 大）", "Add (highest BPM)"), AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
                    slow.Click += (s, e) => pickBySpeed(false);
                    fast.Click += (s, e) => pickBySpeed(true);
                    tips.SetToolTip(slow, string.Format(CultureInfo.CurrentCulture, Lang.T("選んだドリルと同じ方向で、BPM の小さいものから {0} 本を小さい順に追加します。", "Adds the {0} lowest-BPM drills of the selected direction, slowest first."), limit));
                    tips.SetToolTip(fast, string.Format(CultureInfo.CurrentCulture, Lang.T("選んだドリルと同じ方向で、BPM の大きいものから {0} 本を小さい順に追加します。", "Adds the {0} highest-BPM drills of the selected direction, slowest first."), limit));

                    // 方向が決まるまで（行の方向が無く、何も選んでいない間）は押せない
                    Action update = () => slow.Enabled = fast.Enabled = !string.IsNullOrEmpty(speedDirection());
                    list.SelectedIndexChanged += (s, e) => update();
                    update();
                    extra.Add(fast);
                    extra.Add(slow);
                }

                form.Controls.Add(Layout(list, ok, cancel, search, extra));
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                Theme.Apply(form);
                form.Shown += (s, e) => filter.Focus();

                if (form.ShowDialog(owner) != DialogResult.OK) return null;
                if (bySpeed != null) return bySpeed;
                if (list.SelectedItems.Count == 0) return null;
                return list.SelectedItems.Cast<ListViewItem>().OrderBy(i => i.Index).Select(i => (string)i.Tag).ToList();
            }
        }

        private static Form NewDialog(string title, Size size)
        {
            return new Form
            {
                Text = title,
                Icon = AppIcon.Value,
                Font = new Font("Yu Gothic UI", 9f),
                AutoScaleMode = AutoScaleMode.Font,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = size,
            };
        }

        /// <summary>上に（あれば）見出しの行、真ん中に本体、下の右にボタン。</summary>
        /// <param name="extra">OK の左に並べるボタン（右から順に）。</param>
        private static Control Layout(Control body, Button ok, Button cancel, Control top, IEnumerable<Button> extra = null)
        {
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            if (extra != null)
                foreach (var button in extra) buttons.Controls.Add(button);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(body is TextBox ? SizeType.AutoSize : SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (top != null) root.Controls.Add(top, 0, 0);
            root.Controls.Add(body, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            return root;
        }
    }
}
