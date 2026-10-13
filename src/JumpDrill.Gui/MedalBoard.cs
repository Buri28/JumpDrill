using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using JumpDrill.Model;
using JumpDrill.Replays;

namespace JumpDrill.Gui
{
    /// <summary>
    /// メダルの表。行が方向（カスタムステージでは自分で付けた行）、列が Stage。
    ///
    /// 編集できる表（カスタムステージ）では、各行の右端と一番下に「+」のマスを出す。
    /// 押すとドリルを足す。右クリックでマスや行のメニューを出す（中身は画面側が決める）。
    /// </summary>
    internal sealed class MedalBoard : Panel
    {
        public enum HitKind { None, Cell, RowLabel, AddCell, AddRow }

        /// <summary>表のどこを指しているか。</summary>
        public struct Hit
        {
            public HitKind Kind;
            public int Row;
            public int Column;
        }

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

        /// <summary>行名の下に出す印（tower・alt-trip・flick25）の色。方向の名前と見分けが付くように色を変える。</summary>
        private static readonly Color TagColor = Color.FromArgb(0xE0, 0xB0, 0x60);
        private static readonly Color LeftColor = Color.FromArgb(0xD0, 0x3A, 0x3A);

        private static readonly Color GoldColor = Color.FromArgb(0xE0, 0xB4, 0x3C);
        private static readonly Color SilverColor = Color.FromArgb(0xB8, 0xC0, 0xCC);
        private static readonly Color BronzeColor = Color.FromArgb(0xC0, 0x7A, 0x42);

        private static readonly Color PickedColor = Color.FromArgb(0x8C, 0xD8, 0xFF);

        private List<MedalRow> _rows = new List<MedalRow>();
        private bool _editable;
        private readonly ToolTip _tip = new ToolTip();
        private Hit _hover;

        /// <summary>
        /// 選んでいるマスの位置と ID。読み直したとき、同じ位置に同じ譜面があればそのまま、
        /// 無ければ ID で探し直す（並べ替えても選んだものが付いてくる）。
        /// </summary>
        private int _selectedRow = -1;
        private int _selectedColumn = -1;
        private string _selectedName;

        /// <summary>選んでいるマス。無ければ null。</summary>
        public MedalCell Selected
        {
            get { return CellAt(_selectedRow, _selectedColumn); }
        }

        public event EventHandler SelectionChanged;
        public event EventHandler CellDoubleClicked;

        /// <summary>「+」のマスが押された。<see cref="Hit.Row"/> が行の数と同じなら新しい行。</summary>
        public event EventHandler<Hit> AddClicked;

        /// <summary>編集できる表で、マスか行の名前が右クリックされた。<see cref="CellMenus"/> ならマスは編集できなくても出す。</summary>
        public event EventHandler<Hit> MenuRequested;

        /// <summary>編集できない表（その他）でも、マスの右クリックでメニューを出すか。ドリルの削除に使う。</summary>
        public bool CellMenus { get; set; }

        /// <summary>
        /// 編集できる表で、行の名前をつかんで別の位置へ落とした。値は（元の行, 移した後の行）。
        /// </summary>
        public event EventHandler<Tuple<int, int>> RowMoved;

        /// <summary>つかんでいる行。つかんでいなければ -1。</summary>
        private int _dragRow = -1;
        private Point _dragStart;
        private bool _dragging;

        /// <summary>落とす位置（行と行の間の番号。0 なら先頭の上）。</summary>
        private int _dropIndex = -1;

        /// <summary>マウスの位置から、落とす位置（行と行の間）を出す。</summary>
        private int DropIndexAt(Point location)
        {
            int y = location.Y - AutoScrollPosition.Y - HeaderHeight;
            int index = (int)Math.Round(y / (double)RowHeight);
            return Math.Max(0, Math.Min(_rows.Count, index));
        }

        /// <summary>当たったマス。マスでなければ null。</summary>
        public MedalCell CellOf(Hit hit)
        {
            return hit.Kind == HitKind.Cell ? CellAt(hit.Row, hit.Column) : null;
        }

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

        /// <summary>
        /// 窓を合わせる広さ。高さは標準のステージ（14行）までで、それより多い行（「その他」）はスクロールで見せる。
        /// 行が多いたびに窓が縦に伸びると、画面に収まらなくなる。
        /// 幅には縦のスクロールバーのぶんを常に足す。窓の大きさは開いたときにしか決めないので、
        /// あとで行の多いステージに切り替えて縦のバーが出ても、横のバーまで出ないようにする。
        /// </summary>
        public Size ContentSize
        {
            get
            {
                var size = AutoScrollMinSize + new Size(Scaled(8), Scaled(6));
                int cap = HeaderHeight + CustomStage.MaxRows * RowHeight + Scaled(8) + Scaled(6);
                return new Size(size.Width + SystemInformation.VerticalScrollBarWidth, Math.Min(size.Height, cap));
            }
        }

        /// <summary>拡大率が決まってから寸法を出し直す。</summary>
        public void Remeasure()
        {
            SetRows(_rows, _editable);
        }

        public void SetRows(List<MedalRow> rows, bool editable)
        {
            _rows = rows ?? new List<MedalRow>();
            _editable = editable;

            // 表の広さはステージによらず標準（14行 × 8 Stage）に揃える。切り替えても窓の大きさが変わらない。
            // 「+」は満杯でない行・表にしか出さないので、この広さを超えない。
            int columns = Math.Max(CustomStage.MaxDrillsPerRow, _rows.Count == 0 ? 0 : _rows.Max(r => r.Cells.Count + (CanAddCell(r) ? 1 : 0)));
            int count = Math.Max(CustomStage.MaxRows, _rows.Count + (CanAddRow ? 1 : 0));
            AutoScrollMinSize = new Size(LabelWidth + columns * CellWidth + Scaled(16), HeaderHeight + count * RowHeight + Scaled(8));

            // 選んでいたマスを探し直す
            if (_selectedName != null && CellAt(_selectedRow, _selectedColumn)?.SongName != _selectedName)
            {
                _selectedRow = -1;
                for (int r = 0; r < _rows.Count && _selectedRow < 0; r++)
                {
                    int c = _rows[r].Cells.FindIndex(x => x.SongName == _selectedName);
                    if (c >= 0) { _selectedRow = r; _selectedColumn = c; }
                }
                if (_selectedRow < 0) _selectedName = null;
            }

            Invalidate();
        }

        /// <summary>見出しを出す列の数。カスタムステージが空でも Stage 1 から並べて見せる。</summary>
        private int MinColumns { get { return _editable ? 1 : 0; } }

        private bool CanAddCell(MedalRow row)
        {
            return _editable && row.Cells.Count < CustomStage.MaxDrillsPerRow;
        }

        private bool CanAddRow
        {
            get { return _editable && _rows.Count < CustomStage.MaxRows; }
        }

        public void Select(int row, int column)
        {
            _selectedRow = row;
            _selectedColumn = column;
            _selectedName = CellAt(row, column)?.SongName;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearSelection()
        {
            _selectedRow = _selectedColumn = -1;
            _selectedName = null;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private MedalCell CellAt(int row, int column)
        {
            if (row < 0 || row >= _rows.Count || column < 0 || column >= _rows[row].Cells.Count) return null;
            return _rows[row].Cells[column];
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
                int columns = Math.Max(_rows.Count == 0 ? 0 : _rows.Max(r => r.Cells.Count), MinColumns);
                for (int c = 0; c < columns; c++)
                    g.DrawString(Head(c), small, faint, new RectangleF(LabelWidth + c * CellWidth, 0, CellWidth, HeaderHeight), center);

                for (int r = 0; r < _rows.Count; r++)
                {
                    var row = _rows[r];
                    int y = HeaderHeight + r * RowHeight;
                    int width = LabelWidth + (row.Cells.Count + (CanAddCell(row) ? 1 : 0)) * CellWidth;

                    g.DrawLine(line, 0, y, width, y);

                    // 行名は「方向 + 印（tower・alt-trip・flick25）」。1行に並べると印が切れて見えないので、
                    // 印は方向の下に小さく分けて出す。
                    string name, tags;
                    DrillNaming.SplitTags(row.Direction, out name, out tags);

                    float labelWidth = LabelWidth - Scaled(16) - 4 * IconCell;
                    var noWrap = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    g.DrawString(name, bold, text,
                        new RectangleF(Scaled(2), y + Scaled(tags.Length > 0 ? 2 : 6), labelWidth, Scaled(18)), noWrap);
                    if (tags.Length > 0)
                        using (var accent = new SolidBrush(TagColor))
                            g.DrawString(tags, small, accent, new RectangleF(Scaled(4), y + Scaled(19), labelWidth, Scaled(12)), noWrap);
                    g.DrawString(string.Format(CultureInfo.CurrentCulture, "Lv {0} / {1}", row.Level, row.MaxLevel),
                        Font, faint, new PointF(Scaled(4), y + Scaled(tags.Length > 0 ? 29 : 26)));

                    // 名前だけでは向きが読めないので、遷移をグリッドの絵で添える。
                    if (row.Sequences != null && row.Sequences.Count > 0)
                    {
                        int iconX = LabelWidth - Scaled(12) - 4 * IconCell;
                        int iconY = y + (RowHeight - 3 * IconCell) / 2;
                        DrawIcon(g, row.Sequences, row.Tower, iconX, iconY, line);
                    }

                    for (int c = 0; c < row.Cells.Count; c++)
                        DrawCell(g, row.Cells[c], r == _selectedRow && c == _selectedColumn, LabelWidth + c * CellWidth, y, small, text, faint, line, center);

                    if (CanAddCell(row))
                        DrawAdd(g, LabelWidth + row.Cells.Count * CellWidth, y, faint, center, IsHover(HitKind.AddCell, r));
                }

                // 行をつかんで動かしている間は、つかんだ行を薄く塗り、落とす位置に線を引く
                if (_dragging && _dragRow >= 0 && _dropIndex >= 0)
                {
                    using (var held = new SolidBrush(Color.FromArgb(0x30, Theme.HoverText)))
                        g.FillRectangle(held, 0, HeaderHeight + _dragRow * RowHeight, LabelWidth, RowHeight);
                    using (var drop = new Pen(Theme.HoverText, Scaled(2)))
                    {
                        int y = HeaderHeight + _dropIndex * RowHeight;
                        g.DrawLine(drop, 0, y, LabelWidth + Math.Max(1, _rows.Max(r => r.Cells.Count)) * CellWidth, y);
                    }
                }

                if (CanAddRow)
                {
                    int y = HeaderHeight + _rows.Count * RowHeight;
                    g.DrawLine(line, 0, y, LabelWidth + CellWidth, y);
                    using (var hover = new SolidBrush(Theme.HoverText))
                        g.DrawString(Lang.T("+ 行を追加", "+ Add row"), Font, IsHover(HitKind.AddRow, _rows.Count) ? hover : faint,
                            new RectangleF(Scaled(2), y, LabelWidth + CellWidth, RowHeight),
                            new StringFormat { LineAlignment = StringAlignment.Center });
                }
            }
        }

        private bool IsHover(HitKind kind, int row)
        {
            return _hover.Kind == kind && _hover.Row == row;
        }

        /// <summary>列の見出し。標準のステージは 30 秒の Stage に尺を添える。</summary>
        private string Head(int column)
        {
            var entry = _rows.Select(r => column < r.Cells.Count ? r.Cells[column].Entry : null).FirstOrDefault(x => x != null);
            if (entry != null && entry.Options.DurationSeconds > DrillSet.ShortSeconds)
                return string.Format(CultureInfo.CurrentCulture, "Stage {0} ({1:0}s)", entry.Stage, entry.Options.DurationSeconds);
            return string.Format(CultureInfo.CurrentCulture, "Stage {0}", column + 1);
        }

        /// <summary>
        /// 向きの絵。4×3 のグリッドに、右手を青・左手を赤の線と点で描く。
        /// 色は遷移を組む画面（GridPicker）と同じ。
        /// </summary>
        private void DrawIcon(Graphics g, IReadOnlyList<HandSequence> sequences, bool tower, int x, int y, Pen line)
        {
            DrawSequenceIcon(g, sequences, x, y, IconCell, Scaled(2), line, tower);
        }

        /// <summary>向きの絵を描く。ドリルの選択ダイアログの一覧でも同じ絵を使う。</summary>
        /// <param name="cell">グリッドの1マスの大きさ。</param>
        /// <param name="width">遷移の線の太さ。</param>
        /// <param name="tower">タワーのドリル。線を太くし、点の間のノーツがあるマスにも ● を描く。</param>
        internal static void DrawSequenceIcon(Graphics g, IReadOnlyList<HandSequence> sequences, int x, int y, int cell, float width, Pen line,
            bool tower = false)
        {
            if (tower) width *= (float)JumpDrill.Output.SequenceIcon.TowerWidthFactor;

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

                using (var pen = new Pen(color, width))
                {
                    if (points.Length == 2) g.DrawLine(pen, points[0], points[1]);
                    else if (points.Length > 2) g.DrawPolygon(pen, points);
                }

                if (tower)
                    points = JumpDrill.Generation.Tower.NoteCells(seq).Select(p => new PointF(
                        x + (p.LineIndex + 0.5f) * cell,
                        y + (2 - p.LineLayer + 0.5f) * cell + shift)).ToArray();

                float r = cell * 0.28f;
                using (var brush = new SolidBrush(color))
                    foreach (var pt in points)
                        g.FillEllipse(brush, pt.X - r, pt.Y - r, r * 2, r * 2);
            }
        }

        private Rectangle DiscAt(int x, int y)
        {
            return new Rectangle(x + (CellWidth - Disc) / 2, y + Scaled(16), Disc, Disc);
        }

        private void DrawCell(Graphics g, MedalCell cell, bool selected, int x, int y, Font small,
            Brush text, Brush faint, Pen line, StringFormat center)
        {
            if (cell.Bpm.HasValue)
                g.DrawString(string.Format(CultureInfo.InvariantCulture, "{0:0.#} BPM", cell.Bpm.Value), small, faint,
                    new RectangleF(x, y + Scaled(1), CellWidth, Scaled(14)), center);

            // 円の中はベストの再現精度。Lv は色で読める（金 3・銀 2・銅 1）。
            var disc = DiscAt(x, y);
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

            if (selected)
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

        /// <summary>ドリルを足す「+」のマス。点線の円。</summary>
        private void DrawAdd(Graphics g, int x, int y, Brush faint, StringFormat center, bool hover)
        {
            var disc = DiscAt(x, y);
            using (var pen = new Pen(hover ? Theme.HoverText : Theme.Faint) { DashStyle = DashStyle.Dash })
                g.DrawEllipse(pen, disc);
            using (var plus = new Font(Font.FontFamily, 11f))
            using (var hoverBrush = new SolidBrush(Theme.HoverText))
                g.DrawString("+", plus, hover ? hoverBrush : faint, disc, center);
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

            var hit = HitAt(e.Location);
            if (hit.Kind == HitKind.Cell && (hit.Row != _selectedRow || hit.Column != _selectedColumn))
                Select(hit.Row, hit.Column);

            // 行の名前をつかんだら、動かし始めるまでは何もしない（クリックだけなら並べ替えない）
            if (e.Button == MouseButtons.Left && _editable && hit.Kind == HitKind.RowLabel)
            {
                _dragRow = hit.Row;
                _dragStart = e.Location;
            }

            if (e.Button == MouseButtons.Left && (hit.Kind == HitKind.AddCell || hit.Kind == HitKind.AddRow))
                AddClicked?.Invoke(this, hit);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Left && _dragRow >= 0)
            {
                bool dragged = _dragging;
                int from = _dragRow;
                int to = DropIndexAt(e.Location);
                EndDrag();

                // 落とす位置は行の間の番号。自分より下へ落とすと、自分が抜けた分だけ1つ詰まる
                if (dragged)
                {
                    if (to > from) to--;
                    if (to != from) RowMoved?.Invoke(this, Tuple.Create(from, to));
                    return;
                }
            }

            var hit = HitAt(e.Location);
            if (e.Button == MouseButtons.Right &&
                ((_editable && hit.Kind == HitKind.RowLabel) || ((_editable || CellMenus) && hit.Kind == HitKind.Cell)))
                MenuRequested?.Invoke(this, hit);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && HitAt(e.Location).Kind == HitKind.Cell) CellDoubleClicked?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragRow >= 0 && (e.Button & MouseButtons.Left) != 0)
            {
                var size = SystemInformation.DragSize;
                if (!_dragging && (Math.Abs(e.X - _dragStart.X) > size.Width || Math.Abs(e.Y - _dragStart.Y) > size.Height))
                {
                    _dragging = true;
                    Cursor = GrabCursors.Closed;
                    _tip.SetToolTip(this, null);
                }

                if (_dragging)
                {
                    int index = DropIndexAt(e.Location);
                    if (index != _dropIndex)
                    {
                        _dropIndex = index;
                        Invalidate();
                    }
                    return;
                }
            }

            var hit = HitAt(e.Location);
            if (hit.Kind == _hover.Kind && hit.Row == _hover.Row && hit.Column == _hover.Column) return;

            bool redraw = IsAddKind(hit.Kind) || IsAddKind(_hover.Kind);
            _hover = hit;
            // 行の名前はつかんで動かせる。開いた手で知らせる
            Cursor = IsAddKind(hit.Kind) ? Cursors.Hand
                : _editable && hit.Kind == HitKind.RowLabel ? GrabCursors.Open
                : Cursors.Default;
            if (redraw) Invalidate();

            var cell = hit.Kind == HitKind.Cell ? CellAt(hit.Row, hit.Column) : null;
            if (cell == null) { _tip.SetToolTip(this, null); return; }

            string njs = cell.Entry != null
                ? string.Format(CultureInfo.CurrentCulture, "NJS {0:0.#}   ", cell.Entry.Options.Njs) : "";
            _tip.SetToolTip(this, string.Format(CultureInfo.CurrentCulture, "{0}\n{1}{2}",
                DrillNaming.StripId(cell.SongName), njs,
                cell.BestPercent.HasValue
                    ? Lang.T("ベスト ", "Best ") + cell.BestPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%"
                    : Lang.T("未プレイ", "Not played")));
        }

        private void EndDrag()
        {
            bool redraw = _dragging;
            _dragRow = -1;
            _dragging = false;
            _dropIndex = -1;
            Cursor = Cursors.Default;
            if (redraw) Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // ほかの窓に移ったなどでつかみが外れたら、並べ替えずにやめる
            if (!Capture && _dragRow >= 0 && MouseButtons == MouseButtons.None) EndDrag();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (IsAddKind(_hover.Kind)) Invalidate();
            _hover = default(Hit);
        }

        private static bool IsAddKind(HitKind kind)
        {
            return kind == HitKind.AddCell || kind == HitKind.AddRow;
        }

        private Hit HitAt(Point location)
        {
            int x = location.X - AutoScrollPosition.X - Scaled(8);
            int y = location.Y - AutoScrollPosition.Y - HeaderHeight;
            if (x < 0 || y < 0) return default(Hit);

            int r = y / RowHeight;
            if (r == _rows.Count && CanAddRow && x < LabelWidth + CellWidth)
                return new Hit { Kind = HitKind.AddRow, Row = r };
            if (r >= _rows.Count) return default(Hit);

            if (x < LabelWidth) return new Hit { Kind = HitKind.RowLabel, Row = r };

            int c = (x - LabelWidth) / CellWidth;
            if (c < _rows[r].Cells.Count) return new Hit { Kind = HitKind.Cell, Row = r, Column = c };
            if (c == _rows[r].Cells.Count && CanAddCell(_rows[r])) return new Hit { Kind = HitKind.AddCell, Row = r, Column = c };
            return default(Hit);
        }
    }
}
