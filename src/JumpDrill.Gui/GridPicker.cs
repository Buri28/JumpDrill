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

            foreach (var hand in order)
                DrawHand(g, area, hand, cell);

            if (_right.Count == 0 && _left.Count == 0)
                DrawHint(g, area);
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
            var list = ListFor(hand);
            if (list.Count == 0) return;

            bool active = hand == _activeHand;
            Color baseColor = hand == Hand.Right ? RightColor : LeftColor;
            Color color = active ? baseColor : Color.FromArgb(0x60, baseColor);

            float markerRadius = cell * 0.22f;
            float lineWidth = Math.Max(1.5f, cell * 0.045f);

            using (var pen = new Pen(Color.FromArgb(active ? 0xC0 : 0x50, baseColor), lineWidth))
            using (var fill = new SolidBrush(color))
            using (var labelBrush = new SolidBrush(active ? Color.White : Color.FromArgb(0x90, Color.White)))
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
