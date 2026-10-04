using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using JumpDrill.Model;
using JumpDrill.Replays;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 一括生成のドリルのメダルと Lv。1行が1方向、1マスが1本（速さの Stage）。
    ///
    /// 記録は成績（ローカルリーダーボード）と同じ読み方で、ID で引く。
    /// 同じ設定を画面から作って叩いた記録もここに入る。
    ///
    /// メダルは<b>色の付いた円</b>で描く。WinForms（GDI）は絵文字をカラーで描けず、
    /// 🥇🥈🥉 が白黒の輪郭になって見分けが付かない。
    /// </summary>
    public sealed class MedalForm : Form
    {
        private readonly string _workspaceRoot;
        private readonly MedalBoard _board = new MedalBoard { Dock = DockStyle.Fill };
        private readonly Label _total = new Label { AutoSize = true, Font = new Font("Yu Gothic UI", 11f, FontStyle.Bold), Margin = new Padding(0, 0, 16, 0) };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly Button _rescan = new Button { Text = Lang.T("再スキャン", "Rescan"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1) };
        private readonly Button _preview = new Button { Text = Lang.T("ArcViewer でプレビュー", "Preview in ArcViewer"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1), Enabled = false };
        private readonly Button _replay = new Button { Text = Lang.T("スコア", "Scores"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 1, 6, 1), Enabled = false };
        private TableLayoutPanel _top;

        /// <summary>曲名を渡して ArcViewer で開く。</summary>
        private readonly Action<IWin32Window, string> _openPreview;

        /// <summary>譜面の ID を渡して、その譜面を選んだ状態でスコア画面を開く（開いていれば前に出す）。</summary>
        private readonly Action<string> _openReplay;

        /// <summary>スコア画面が開いていれば、その譜面に切り替える。前には出さない。</summary>
        private readonly Action<string> _followReplay;

        /// <summary>記録を読む Beat Saber。</summary>
        private readonly InstallPicker _install;

        /// <summary>読んでいる間に見る版が変わった。読み終えたら読み直す。</summary>
        private bool _reloadPending;

        internal MedalForm(string workspaceRoot, SettingsStore settings, Action<IWin32Window, string> openPreview, Action<string> openReplay, Action<string> followReplay)
        {
            _workspaceRoot = workspaceRoot;
            _install = new InstallPicker(settings) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 8, 3) };
            _openPreview = openPreview;
            _openReplay = openReplay;
            _followReplay = followReplay;

            Text = Lang.T("JumpDrill — メダル", "JumpDrill — Medals");
            Icon = AppIcon.Value;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Yu Gothic UI", 9f);
            MinimumSize = new Size(560, 360);
            Size = new Size(820, 680);
            StartPosition = FormStartPosition.Manual;

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, AutoSize = true, Padding = new Padding(8, 8, 8, 4) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(_install, 0, 0);
            top.Controls.Add(_total, 1, 0);
            top.Controls.Add(_replay, 3, 0);
            top.Controls.Add(_preview, 4, 0);
            top.Controls.Add(_rescan, 5, 0);
            // 状態の文は2段目に横いっぱいで出す。1段目は版の欄とボタンで埋まり、並べると切れる
            top.Controls.Add(_status, 0, 1);
            top.SetColumnSpan(_status, 6);

            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.Controls.Add(top, 0, 0);
            outer.Controls.Add(_board, 0, 1);
            Controls.Add(outer);
            _top = top;

            Theme.Apply(this);

            _rescan.Click += async (s, e) => await ScanAsync();
            Shown += async (s, e) => await ScanAsync();

            // 見る版を変えたら読み直す（スコア画面で変えたときも）
            // 読んでいる最中なら、読み終えてからもう一度読む
            Action reload = async () =>
            {
                if (IsDisposed) return;
                if (!_rescan.Enabled) { _reloadPending = true; return; }
                await ScanAsync();
            };
            InstallPicker.Changed += reload;
            FormClosed += (s, e) => InstallPicker.Changed -= reload;
            Load += (s, e) => _install.FitWidth(DeviceDpi);

            // マスを選んでボタンを押す。ダブルクリックはスコア画面。スコアはプレイしたマスだけ。
            // スコア画面を開いたままなら、マスを選ぶだけでそちらも切り替わる。
            _board.SelectionChanged += (s, e) =>
            {
                var cell = _board.Selected;
                _preview.Enabled = cell != null;
                _replay.Enabled = cell != null && cell.BestPercent.HasValue;
                if (_replay.Enabled && _followReplay != null) _followReplay(cell.Id);
            };
            _preview.Click += (s, e) => OpenPreview();
            _replay.Click += (s, e) => OpenReplay();
            _board.CellDoubleClicked += (s, e) => OpenReplay();

            // 表の大きさは方向の数と画面の拡大率で決まるので、決め打ちせず中身に合わせる。
            Load += (s, e) => FitToContent();

            // 叩く前でも空の表は見せる。どこを埋めればいいかがそのまま次の練習になる。
            ShowRows(DrillSetMedals.Evaluate(new Dictionary<string, double>()));
        }

        private async Task ScanAsync()
        {
            _rescan.Enabled = false;
            _status.Text = Lang.T("リプレイを読み込んでいます...", "Loading replays...");

            try
            {
                int failed = 0;
                string workspace = _workspaceRoot;
                string install = InstallPicker.Selected;
                var best = await Task.Run(() => DrillSetMedals.BestById(DrillRecords.Load(workspace, (path, ex) => failed++, install)));

                var rows = DrillSetMedals.Evaluate(best);
                ShowRows(rows);

                int medals = rows.Sum(r => r.Cells.Count(c => c.Medal != Medal.None));
                int total = rows.Sum(r => r.Cells.Count);
                _status.Text = string.Format(CultureInfo.CurrentCulture, Lang.T("メダル数 {0} / {1}{2}", "Medals {0} / {1}{2}"),
                    medals, total, failed > 0 ? Lang.T("   読み込めなかったリプレイ: " + failed + " 件", "   Unreadable replays: " + failed) : "");
            }
            catch (Exception ex)
            {
                _status.Text = Lang.T("読み込みに失敗しました: ", "Failed to load: ") + ex.Message;
            }
            finally
            {
                _rescan.Enabled = true;
            }

            if (_reloadPending && !IsDisposed)
            {
                _reloadPending = false;
                await ScanAsync();
            }
        }

        /// <summary>
        /// 表が全部入り、スクロールの出ない大きさにする。画面に入らなければ画面いっぱいまで。
        /// </summary>
        private void FitToContent()
        {
            // 寸法は画面の拡大率で変わる。窓ができてから測り直す。
            _board.Remeasure();
            PerformLayout();

            // 余白や見出しの高さを足し合わせるより、いま見えている広さとの差で合わせる方が確か。
            // 1回目はスクロールバーが出ている分だけ広く取るので、消えたあとでもう一度詰める。
            Rectangle area = Screen.FromControl(this).WorkingArea;
            var outer = Size;
            for (int pass = 0; pass < 2; pass++)
            {
                Size diff = _board.ContentSize - _board.ClientSize;
                outer = new Size(
                    Math.Min(Math.Max(Width + diff.Width, MinimumSize.Width), area.Width),
                    Math.Min(Math.Max(Height + diff.Height, MinimumSize.Height), area.Height));
                Size = outer;
                PerformLayout();
            }

            // 大きさを変えたので置き直す。親の中央、はみ出すなら画面の内側へ。
            Rectangle around = Owner != null ? Owner.Bounds : area;
            int x = around.Left + (around.Width - outer.Width) / 2;
            int y = around.Top + (around.Height - outer.Height) / 2;
            Location = new Point(
                Math.Max(area.Left, Math.Min(x, area.Right - outer.Width)),
                Math.Max(area.Top, Math.Min(y, area.Bottom - outer.Height)));
        }

        private void OpenPreview()
        {
            var cell = _board.Selected;
            if (cell == null || _openPreview == null) return;
            _openPreview(this, cell.Entry.Options.Name);
        }

        private void OpenReplay()
        {
            var cell = _board.Selected;
            if (cell == null || !cell.BestPercent.HasValue || _openReplay == null) return;
            _openReplay(cell.Id);
        }

        private void ShowRows(List<MedalRow> rows)
        {
            _total.Text = string.Format(CultureInfo.CurrentCulture, Lang.T("総合 Lv {0} / {1}", "Total Lv {0} / {1}"),
                rows.Sum(r => r.Level), DrillSetMedals.MaxLevel);
            _board.SetRows(rows);
        }

        /// <summary>メダルの表。行が方向、列が Stage。</summary>
        private sealed class MedalBoard : Panel
        {
            // 96 dpi での寸法。文字は画面の拡大率で大きくなるので、枠も同じ率で広げる
            // （広げないと「133.3 BPM」や「Stage 8 (30s)」が欠ける）。
            private int LabelWidth { get { return Scaled(128); } }
            private int CellWidth { get { return Scaled(62); } }
            private int RowHeight { get { return Scaled(48); } }
            private int HeaderHeight { get { return Scaled(18); } }
            private int Disc { get { return Scaled(30); } }

            /// <summary>向きの絵の1マス。4×3 のグリッドを描く。</summary>
            private int IconCell { get { return Scaled(9); } }

            private static readonly Color RightColor = Color.FromArgb(0x2E, 0x86, 0xD8);
            private static readonly Color LeftColor = Color.FromArgb(0xD0, 0x3A, 0x3A);

            private static readonly Color GoldColor = Color.FromArgb(0xE0, 0xB4, 0x3C);
            private static readonly Color SilverColor = Color.FromArgb(0xB8, 0xC0, 0xCC);
            private static readonly Color BronzeColor = Color.FromArgb(0xC0, 0x7A, 0x42);

            private static readonly Color PickedColor = Color.FromArgb(0x8C, 0xD8, 0xFF);

            private List<MedalRow> _rows = new List<MedalRow>();
            private readonly ToolTip _tip = new ToolTip();
            private MedalCell _hover;

            /// <summary>選んでいるマスの ID。読み直してもこれで選び直す。</summary>
            private string _selectedId;

            /// <summary>選んでいるマス。無ければ null。</summary>
            public MedalCell Selected
            {
                get { return _rows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Id == _selectedId); }
            }

            public event EventHandler SelectionChanged;
            public event EventHandler CellDoubleClicked;

            private int Scaled(int value)
            {
                return (int)Math.Round(value * DeviceDpi / 96.0);
            }

            public MedalBoard()
            {
                DoubleBuffered = true;
                AutoScroll = true;
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            /// <summary>表全体の大きさ（スクロールなしで見せるのに要る広さ）。</summary>
            public Size ContentSize
            {
                get { return AutoScrollMinSize + new Size(Scaled(8), Scaled(6)); }
            }

            /// <summary>拡大率が決まってから寸法を出し直す。</summary>
            public void Remeasure()
            {
                SetRows(_rows);
            }

            public void SetRows(List<MedalRow> rows)
            {
                _rows = rows ?? new List<MedalRow>();
                int columns = _rows.Count == 0 ? 0 : _rows.Max(r => r.Cells.Count);
                AutoScrollMinSize = new Size(LabelWidth + columns * CellWidth + Scaled(16), HeaderHeight + _rows.Count * RowHeight + Scaled(8));
                Invalidate();
            }

            protected override void OnScroll(ScrollEventArgs se)
            {
                base.OnScroll(se);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(AutoScrollPosition.X + Scaled(8), AutoScrollPosition.Y);

                using (var faint = new SolidBrush(Theme.Faint))
                using (var text = new SolidBrush(Theme.Text))
                using (var line = new Pen(Theme.Line))
                using (var bold = new Font(Font.FontFamily, 9f, FontStyle.Bold))
                using (var small = new Font(Font.FontFamily, 6.5f))
                {
                    var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

                    // Stage の見出し。BPM は方向ごとに倍率が違うのでマスの中に書き、ここは Stage だけ。
                    if (_rows.Count > 0)
                    {
                        var cells = _rows[0].Cells;
                        for (int c = 0; c < cells.Count; c++)
                        {
                            var entry = cells[c].Entry;
                            string head = entry.Options.DurationSeconds > DrillSet.ShortSeconds
                                ? string.Format(CultureInfo.CurrentCulture, "Stage {0} ({1:0}s)", entry.Stage, entry.Options.DurationSeconds)
                                : string.Format(CultureInfo.CurrentCulture, "Stage {0}", entry.Stage);
                            g.DrawString(head, small, faint, new RectangleF(LabelWidth + c * CellWidth, 0, CellWidth, HeaderHeight), center);
                        }
                    }

                    for (int r = 0; r < _rows.Count; r++)
                    {
                        var row = _rows[r];
                        int y = HeaderHeight + r * RowHeight;

                        g.DrawLine(line, 0, y, LabelWidth + row.Cells.Count * CellWidth, y);
                        g.DrawString(row.Direction, bold, text, new PointF(Scaled(2), y + Scaled(6)));
                        g.DrawString(string.Format(CultureInfo.CurrentCulture, "Lv {0} / {1}",
                            row.Level, row.Cells.Count * DrillSetMedals.Points(Medal.Gold)), Font, faint, new PointF(Scaled(4), y + Scaled(26)));

                        // 名前だけでは向きが読めないので、遷移をグリッドの絵で添える。
                        int iconX = LabelWidth - Scaled(12) - 4 * IconCell;
                        int iconY = y + (RowHeight - 3 * IconCell) / 2;
                        DrawIcon(g, row.Cells[0].Entry.Options.Sequences, iconX, iconY, line);

                        for (int c = 0; c < row.Cells.Count; c++)
                            DrawCell(g, row.Cells[c], LabelWidth + c * CellWidth, y, small, text, faint, line, center);
                    }
                }
            }

            /// <summary>
            /// 向きの絵。4×3 のグリッドに、右手を青・左手を赤の線と点で描く。
            /// 色は遷移を組む画面（GridPicker）と同じ。
            /// </summary>
            private void DrawIcon(Graphics g, IReadOnlyList<HandSequence> sequences, int x, int y, Pen line)
            {
                int cell = IconCell;
                for (int col = 0; col < 4; col++)
                    for (int layer = 0; layer < 3; layer++)
                        g.DrawRectangle(line, x + col * cell, y + layer * cell, cell, cell);

                foreach (var seq in sequences)
                {
                    Color color = seq.Hand == Hand.Right ? RightColor : LeftColor;

                    // 横の遷移（85・c9 など）は左右の手が同じマスを通り、線が重なって片方が消える。
                    // 右手を少し上、左手を少し下にずらして両方見えるようにする。
                    float shift = (seq.Hand == Hand.Right ? -0.18f : 0.18f) * cell;
                    var points = seq.Steps.Select(st => new PointF(
                        x + (st.Position.LineIndex + 0.5f) * cell,
                        y + (2 - st.Position.LineLayer + 0.5f) * cell + shift)).ToArray();

                    using (var pen = new Pen(color, Scaled(2)))
                    {
                        if (points.Length == 2) g.DrawLine(pen, points[0], points[1]);
                        else if (points.Length > 2) g.DrawPolygon(pen, points);
                    }

                    float r = cell * 0.28f;
                    using (var brush = new SolidBrush(color))
                        foreach (var pt in points)
                            g.FillEllipse(brush, pt.X - r, pt.Y - r, r * 2, r * 2);
                }
            }

            private void DrawCell(Graphics g, MedalCell cell, int x, int y, Font small,
                Brush text, Brush faint, Pen line, StringFormat center)
            {
                g.DrawString(string.Format(CultureInfo.InvariantCulture, "{0:0.#} BPM", cell.Entry.Bpm), small, faint,
                    new RectangleF(x, y + Scaled(1), CellWidth, Scaled(14)), center);

                // 円の中はベストの再現精度。Lv は色で読める（金 3・銀 2・銅 1）。
                var disc = new Rectangle(x + (CellWidth - Disc) / 2, y + Scaled(16), Disc, Disc);
                Color? color = ColorOf(cell.Medal);
                if (color.HasValue)
                {
                    using (var fill = new SolidBrush(color.Value))
                        g.FillEllipse(fill, disc);
                }
                else
                {
                    g.DrawEllipse(line, disc);
                }

                if (cell.Id == _selectedId)
                {
                    int pad = Scaled(3);
                    using (var ring = new Pen(PickedColor, Scaled(2)))
                        g.DrawEllipse(ring, disc.X - pad, disc.Y - pad, disc.Width + pad * 2, disc.Height + pad * 2);
                }

                if (!cell.BestPercent.HasValue) return;

                using (var number = new Font(small.FontFamily, 6.5f, FontStyle.Bold))
                using (var dark = new SolidBrush(Color.FromArgb(0x20, 0x20, 0x20)))
                    g.DrawString(cell.BestPercent.Value.ToString("0.0", CultureInfo.InvariantCulture), number,
                        color.HasValue ? dark : text, disc, center);
            }

            private static Color? ColorOf(Medal medal)
            {
                switch (medal)
                {
                    case Medal.Gold: return GoldColor;
                    case Medal.Silver: return SilverColor;
                    case Medal.Bronze: return BronzeColor;
                    default: return null;
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);

                var cell = CellAt(e.Location);
                if (cell == null || cell.Id == _selectedId) return;

                _selectedId = cell.Id;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                if (CellAt(e.Location) != null) CellDoubleClicked?.Invoke(this, EventArgs.Empty);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);

                var cell = CellAt(e.Location);
                if (cell == _hover) return;
                _hover = cell;

                if (cell == null) { _tip.SetToolTip(this, null); return; }

                _tip.SetToolTip(this, string.Format(CultureInfo.CurrentCulture,
                    "{0}\nNJS {1:0.#}   {2}",
                    cell.Entry.Options.Name, cell.Entry.Options.Njs,
                    cell.BestPercent.HasValue
                        ? Lang.T("ベスト ", "Best ") + cell.BestPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%"
                        : Lang.T("未プレイ", "Not played")));
            }

            private MedalCell CellAt(Point location)
            {
                int x = location.X - AutoScrollPosition.X - Scaled(8) - LabelWidth;
                int y = location.Y - AutoScrollPosition.Y - HeaderHeight;
                if (x < 0 || y < 0) return null;

                int r = y / RowHeight;
                int c = x / CellWidth;
                if (r >= _rows.Count || c >= _rows[r].Cells.Count) return null;
                return _rows[r].Cells[c];
            }
        }
    }
}
