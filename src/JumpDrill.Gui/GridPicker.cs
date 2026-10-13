using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using JumpDrill.Model;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 4x3 のグリッドを直接クリックして遷移を組むコントロール。
    /// 記法を覚えていなくても配置を作れるようにするのが目的。
    /// 左クリックで点を置き、もう一度押すと外す。右クリックでその手を空に。
    /// 置けるのは<b>片手2点まで</b>。埋まっているところへ別の点を置くと置き直しになる。
    ///
    /// <b>Ctrl+左クリック</b>でその点の <c>s</c>（矢印を上下左右に倒す）を入り切りする。
    /// 倒した点は<b>四角</b>で描く（Straight / Square の s）。
    /// </summary>
    public sealed class GridPicker : Control
    {
        private static readonly Color Background = Color.FromArgb(0x14, 0x16, 0x1A);
        private static readonly Color CellOutline = Color.FromArgb(0x2A, 0x2E, 0x36);
        private static readonly Color CellText = Color.FromArgb(0x5A, 0x60, 0x6C);
        private static readonly Color RightColor = Color.FromArgb(0x2E, 0x86, 0xD8);
        private static readonly Color LeftColor = Color.FromArgb(0xD0, 0x3A, 0x3A);

        /// <summary>左右の手が同じマス・同じ線を使うところの色（赤と青の間）。</summary>
        private static readonly Color OverlapColor = Color.FromArgb(0x9B, 0x59, 0xD0);

        // 点ごとに s を持つので、位置だけでなく SequenceStep のまま抱える。
        private readonly List<SequenceStep> _right = new List<SequenceStep>();
        private readonly List<SequenceStep> _left = new List<SequenceStep>();

        private Hand _activeHand = Hand.Right;

        /// <summary>
        /// 片手あたりに置ける点の数。
        ///
        /// このツールが出すのは「2点の間をどれだけ直線で振れたか」なので、
        /// 要るのは行き先と戻り先の2点だけ。3点以上の循環を置けても測る対象が増えない。
        /// </summary>
        public const int MaxSteps = 2;

        public GridPicker()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Background;
        }

        /// <summary>どちらの手の循環を編集するか。</summary>
        public Hand ActiveHand
        {
            get { return _activeHand; }
            set
            {
                if (_activeHand == value) return;
                _activeHand = value;
                Invalidate();
            }
        }

        private bool _tower;

        /// <summary>
        /// タワーで作るか。線を太くし、点の間のノーツがあるマスにも ●（番号なし）を出す。
        /// </summary>
        public bool Tower
        {
            get { return _tower; }
            set
            {
                if (_tower == value) return;
                _tower = value;
                Invalidate();
            }
        }

        private bool _mirror;

        /// <summary>
        /// 左右反転して反対の手にも同じ形を組むか。片手だけ組んでいるとき、反対の手に反転した形を薄く出す
        /// （押しても置けない。生成のときに作られる形を見せるだけ）。
        /// </summary>
        public bool Mirror
        {
            get { return _mirror; }
            set
            {
                if (_mirror == value) return;
                _mirror = value;
                Invalidate();
            }
        }

        public event EventHandler SelectionChanged;

        private List<SequenceStep> ListFor(Hand hand)
        {
            return hand == Hand.Right ? _right : _left;
        }

        public IReadOnlyList<SequenceStep> StepsFor(Hand hand)
        {
            return ListFor(hand);
        }

        private static int IndexOf(List<SequenceStep> list, GridPosition position)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Position == position) return i;
            return -1;
        }

        /// <summary>点ごとの <c>s</c> を入り切りする。ボタン側から使う。</summary>
        public void SetStraighten(Hand hand, int index, bool straighten)
        {
            var list = ListFor(hand);
            if (index < 0 || index >= list.Count) return;

            var step = list[index];
            if (step.Straighten == straighten) return;

            list[index] = new SequenceStep(step.Position, step.ExplicitDirection, straighten);
            OnSelectionChanged();
        }

        public void Clear(Hand hand)
        {
            if (ListFor(hand).Count == 0) return;
            ListFor(hand).Clear();
            OnSelectionChanged();
        }

        public void ClearAll()
        {
            if (_right.Count == 0 && _left.Count == 0) return;
            _right.Clear();
            _left.Clear();
            OnSelectionChanged();
        }

        /// <summary>記法テキスト側で編集されたときに picker の状態を合わせる。</summary>
        public void SetFrom(IEnumerable<HandSequence> sequences)
        {
            _right.Clear();
            _left.Clear();

            if (sequences != null)
            {
                foreach (var seq in sequences)
                    ListFor(seq.Hand).AddRange(seq.Steps);
            }

            Invalidate();
        }

        private void OnSelectionChanged()
        {
            Invalidate();
            var handler = SelectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button == MouseButtons.Right)
            {
                Clear(_activeHand);
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            GridPosition hit;
            if (!TryHitTest(e.Location, out hit)) return;

            var list = ListFor(_activeHand);
            int existing = IndexOf(list, hit);

            // Ctrl は消す操作ではなく s の入り切り。まだ無い点なら s 付きで置く。
            bool straighten = (ModifierKeys & Keys.Control) == Keys.Control;

            if (existing >= 0)
            {
                if (straighten)
                {
                    var step = list[existing];
                    list[existing] = new SequenceStep(step.Position, step.ExplicitDirection, !step.Straighten);
                }
                else
                {
                    list.RemoveAt(existing);
                }
            }
            else
            {
                // 2点が埋まっているところへ別の点を置いたら、そこから置き直しとみなす。
                // 古い方を押し出すと、直したかった点まで消えて分かりにくい。
                if (list.Count >= MaxSteps) list.Clear();

                list.Add(new SequenceStep(hit, null, straighten));
            }

            OnSelectionChanged();
        }

        private bool TryHitTest(Point point, out GridPosition position)
        {
            position = default(GridPosition);

            RectangleF area = GridArea();
            float cellW = area.Width / GridPosition.Columns;
            float cellH = area.Height / GridPosition.Rows;

            if (!area.Contains(point)) return false;

            int column = (int)((point.X - area.X) / cellW);
            int row = (int)((point.Y - area.Y) / cellH);
            column = Math.Min(GridPosition.Columns - 1, Math.Max(0, column));
            row = Math.Min(GridPosition.Rows - 1, Math.Max(0, row));

            position = new GridPosition(column, GridPosition.Rows - 1 - row);
            return true;
        }

        /// <summary>常に 4:3 の正方セルを保つ。</summary>
        private RectangleF GridArea()
        {
            const float Padding = 8f;
            float w = Math.Max(1f, Width - Padding * 2);
            float h = Math.Max(1f, Height - Padding * 2);

            float cell = Math.Min(w / GridPosition.Columns, h / GridPosition.Rows);
            float gridW = cell * GridPosition.Columns;
            float gridH = cell * GridPosition.Rows;

            return new RectangleF((Width - gridW) / 2f, (Height - gridH) / 2f, gridW, gridH);
        }

        private RectangleF CellRect(GridPosition p, RectangleF area)
        {
            float cellW = area.Width / GridPosition.Columns;
            float cellH = area.Height / GridPosition.Rows;
            return new RectangleF(
                area.X + p.LineIndex * cellW,
                area.Y + (GridPosition.Rows - 1 - p.LineLayer) * cellH,
                cellW, cellH);
        }

        private PointF CellCenter(GridPosition p, RectangleF area)
        {
            var r = CellRect(p, area);
            return new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Background);

            RectangleF area = GridArea();
            float cell = area.Width / GridPosition.Columns;
            float inset = cell * 0.07f;

            using (var outline = new Pen(CellOutline, Math.Max(1f, cell * 0.03f)))
            using (var textBrush = new SolidBrush(CellText))
            using (var font = new Font("Consolas", Math.Max(7f, cell * 0.22f), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Near,
                };

                for (int layer = 0; layer < GridPosition.Rows; layer++)
                {
                    for (int index = 0; index < GridPosition.Columns; index++)
                    {
                        var p = new GridPosition(index, layer);
                        var r = CellRect(p, area);
                        r.Inflate(-inset, -inset);
                        g.DrawRectangle(outline, r.X, r.Y, r.Width, r.Height);

                        // 記法の記号を隅に出しておくと CLI に持ち替えるときに困らない。
                        g.DrawString(p.ToToken().ToString(), font, textBrush,
                            new PointF(r.X + inset * 0.6f, r.Y + inset * 0.4f), format);
                    }
                }
            }

            // 非アクティブな手を先に描いて、編集中の手を前面に出す。
            var order = _activeHand == Hand.Right
                ? new[] { Hand.Left, Hand.Right }
                : new[] { Hand.Right, Hand.Left };

            // 反転で作られる反対の手は、組んでいる手の後ろに薄く描く
            var ghost = GhostSteps();
            if (ghost != null)
                DrawSteps(g, area, _right.Count > 0 ? Hand.Left : Hand.Right, cell, ghost, ghost: true);

            foreach (var hand in order)
                DrawHand(g, area, hand, cell);

            DrawOverlap(g, area, cell, ghost);

            if (_right.Count == 0 && _left.Count == 0)
                DrawHint(g, area);
        }

        /// <summary>反転で作られる反対の手の点。片手だけ組んでいて反転がオンのときだけ。それ以外は null。</summary>
        private List<SequenceStep> GhostSteps()
        {
            if (!_mirror || (_right.Count == 0) == (_left.Count == 0)) return null;

            var source = _right.Count > 0 ? _right : _left;
            return source
                .Select(st => new SequenceStep(new GridPosition(GridPosition.Columns - 1 - st.Position.LineIndex, st.Position.LineLayer), null, st.Straighten))
                .ToList();
        }

        /// <summary>
        /// 左右の手が同じマス・同じ線を使うところを紫で塗り直す。反転で重なる配置（R41 と L14 など）に気付けるように。
        /// 反転の薄い形も相手の手として数える。
        /// </summary>
        private void DrawOverlap(Graphics g, RectangleF area, float cell, List<SequenceStep> ghost)
        {
            var right = _right.Count > 0 ? _right : ghost ?? new List<SequenceStep>();
            var left = _left.Count > 0 ? _left : ghost ?? new List<SequenceStep>();
            if (right.Count == 0 || left.Count == 0) return;

            float markerRadius = cell * 0.22f;
            float lineWidth = Math.Max(1.5f, cell * 0.045f);
            if (_tower) lineWidth *= (float)JumpDrill.Output.SequenceIcon.TowerWidthFactor;

            using (var pen = new Pen(OverlapColor, lineWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var fill = new SolidBrush(OverlapColor))
            using (var labelBrush = new SolidBrush(Color.White))
            using (var labelFont = new Font(Font.FontFamily, Math.Max(7f, cell * 0.2f), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                // 線：両手の遷移が同じ直線で2マス以上重なるところ
                foreach (var a in Segments(right))
                    foreach (var b in Segments(left))
                    {
                        var cellsA = JumpDrill.Generation.Tower.Cells(a[0], a[1]);
                        var cellsB = JumpDrill.Generation.Tower.Cells(b[0], b[1]);
                        if (cellsA == null || cellsB == null) continue;
                        var common = cellsA.Where(c => cellsB.Contains(c)).ToList();
                        if (common.Count >= 2)
                            g.DrawLine(pen, CellCenter(common[0], area), CellCenter(common[common.Count - 1], area));
                    }

                // 点：両手のノーツが同じマスにあるところ。番号は編集中の手のものを出す
                var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var active = _activeHand == Hand.Right ? right : left;
                var cellsLeft = NoteCellsOf(left);
                foreach (var p in NoteCellsOf(right).Where(c => cellsLeft.Contains(c)))
                {
                    var c = CellCenter(p, area);
                    var box = new RectangleF(c.X - markerRadius, c.Y - markerRadius, markerRadius * 2, markerRadius * 2);
                    g.FillEllipse(fill, box);
                    int index = IndexOf(active, p);
                    if (index >= 0) g.DrawString((index + 1).ToString(), labelFont, labelBrush, box, format);
                }
            }
        }

        /// <summary>その手のノーツが置かれるマス（タワーなら点の間も）。</summary>
        private List<GridPosition> NoteCellsOf(List<SequenceStep> list)
        {
            if (_tower && list.Count >= 2)
                return JumpDrill.Generation.Tower.NoteCells(new HandSequence(Hand.Right, list));
            return list.Select(st => st.Position).ToList();
        }

        /// <summary>循環の線分。2点なら1本。</summary>
        private static IEnumerable<GridPosition[]> Segments(List<SequenceStep> list)
        {
            if (list.Count < 2) yield break;
            int edges = list.Count == 2 ? 1 : list.Count;
            for (int i = 0; i < edges; i++)
                yield return new[] { list[i % list.Count].Position, list[(i + 1) % list.Count].Position };
        }

        private void DrawHint(Graphics g, RectangleF area)
        {
            using (var brush = new SolidBrush(Color.FromArgb(0x70, 0x78, 0x88)))
            using (var font = new Font(Font.FontFamily, Math.Max(8f, area.Width * 0.028f)))
            {
                var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(Lang.T("マスをクリックして遷移を組む\n"
                    + "Ctrl+クリックで矢印を上下左右に倒す（四角）\n"
                    + "右クリックでその手を消す",
                    "Click cells to build the sequence\n"
                    + "Ctrl+click snaps the arrow straight (square)\n"
                    + "Right-click clears that hand"), font, brush, area, format);
            }
        }

        private void DrawHand(Graphics g, RectangleF area, Hand hand, float cell)
        {
            DrawSteps(g, area, hand, cell, ListFor(hand), ghost: false);
        }

        /// <param name="ghost">反転で作られる形。編集中でない手よりさらに薄く描く。</param>
        private void DrawSteps(Graphics g, RectangleF area, Hand hand, float cell, List<SequenceStep> list, bool ghost)
        {
            if (list.Count == 0) return;

            bool active = !ghost && hand == _activeHand;
            Color baseColor = hand == Hand.Right ? RightColor : LeftColor;
            Color color = active ? baseColor : Color.FromArgb(ghost ? 0x48 : 0x60, baseColor);

            float markerRadius = cell * 0.22f;
            float lineWidth = Math.Max(1.5f, cell * 0.045f);
            if (_tower) lineWidth *= (float)JumpDrill.Output.SequenceIcon.TowerWidthFactor;

            using (var pen = new Pen(Color.FromArgb(active ? 0xC0 : ghost ? 0x40 : 0x50, baseColor), lineWidth))
            using (var fill = new SolidBrush(color))
            using (var labelBrush = new SolidBrush(active ? Color.White : Color.FromArgb(ghost ? 0x70 : 0x90, Color.White)))
            using (var labelFont = new Font(Font.FontFamily, Math.Max(7f, cell * 0.2f), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                pen.EndCap = LineCap.Round;
                pen.StartCap = LineCap.Round;

                // 循環なので末尾から先頭に戻る線も引く。2点なら往復1本で足りる。
                int edges = list.Count == 2 ? 1 : list.Count;
                if (list.Count >= 2)
                {
                    for (int i = 0; i < edges; i++)
                    {
                        var from = CellCenter(list[i % list.Count].Position, area);
                        var to = CellCenter(list[(i + 1) % list.Count].Position, area);
                        g.DrawLine(pen, from, to);
                    }
                }

                // タワーでは点の間のマスにもノーツが並ぶ。
                if (_tower && list.Count >= 2)
                {
                    var steps = new HandSequence(hand, list);
                    foreach (var p in JumpDrill.Generation.Tower.NoteCells(steps))
                    {
                        if (IndexOf(list, p) >= 0) continue;
                        var c = CellCenter(p, area);
                        g.FillEllipse(fill, c.X - markerRadius, c.Y - markerRadius, markerRadius * 2, markerRadius * 2);
                    }
                }

                var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                for (int i = 0; i < list.Count; i++)
                {
                    var c = CellCenter(list[i].Position, area);
                    var box = new RectangleF(c.X - markerRadius, c.Y - markerRadius, markerRadius * 2, markerRadius * 2);

                    // s の点は四角。丸との差なので、色を足さずに一目で分かる。
                    if (list[i].Straighten) g.FillRectangle(fill, box);
                    else g.FillEllipse(fill, box);

                    // 叩く順番。3点以上の循環では向きが読めないと意味が無い。
                    g.DrawString((i + 1).ToString(), labelFont, labelBrush, box, format);
                }
            }
        }
    }
}
