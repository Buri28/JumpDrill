using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using JumpDrill.Replays;
using Note = JumpDrill.Replays.NoteFigure;
using Swing = JumpDrill.Replays.SwingScene.Swing;

namespace JumpDrill.Gui
{
    /// <summary>
    /// リプレイの軌道を描く。
    ///
    /// 振りを全部重ねて表示するので、<b>再現性がそのまま束の太さとして見える</b>。
    /// 数値（RMS）で出しているものを目で確かめるための画面。
    ///
    /// 描くのは<b>ブレード先端</b>。リプレイに入っているのはコントローラの位置で、
    /// そこはノーツから1 m ほど離れているので、そのまま描くとノーツと噛み合わない。
    ///
    /// 先端は世界座標だが、ノーツの格子はプレイヤーの正面に立っているので、
    /// X-Y 平面へ落とすとそのまま格子と重なる。
    /// </summary>
    public sealed class ReplayView : Control
    {
        /// <summary>どの向きから見た図か。</summary>
        public enum ViewAxis
        {
            /// <summary>正面（X-Y）。ノーツの格子がそのまま出る。</summary>
            Front = 0,

            /// <summary>
            /// 横（Z-Y）。<b>奥行きが見える。</b>
            ///
            /// 正面図では、振りかぶりも振り抜きも画面の手前・奥に潰れて見えない。
            /// PRE / POST が低いのに絵では真っ直ぐに見える、ということが起きる。
            /// 横から見ると、打点の手前でどれだけ引けているか（PRE）と、
            /// 打点の先までどれだけ振り抜けているか（POST）がそのまま長さになる。
            /// </summary>
            Side = 1,
        }

        /// <summary>どの向きから見るか。<see cref="Load"/> の前後どちらで変えてもよい。</summary>
        public ViewAxis Axis
        {
            get { return _axis; }
            set
            {
                if (_axis == value) return;
                _axis = value;
                ComputeBounds();
                Invalidate();
            }
        }

        private ViewAxis _axis;

        /// <summary>
        /// 横から見た図で、<b>プレイヤーが右を向いているか</b>。横の図でしか効かない。
        ///
        /// true なら奥（ノーツの来る方）が右、プレイヤーが左。振りは右へ出ていく。
        /// false はその左右反転。どちらが読みやすいかは利き手と見慣れで変わるので、
        /// 切り替えられるようにしてある。
        /// </summary>
        public bool FaceRight
        {
            get { return _faceRight; }
            set
            {
                if (_faceRight == value) return;
                _faceRight = value;
                ComputeBounds();
                Invalidate();
            }
        }

        private bool _faceRight = true;

        private static readonly Color Background = Color.FromArgb(0x14, 0x16, 0x1A);
        private static readonly Color GridColor = Color.FromArgb(0x2A, 0x2E, 0x36);
        private static readonly Color RightColor = SwingColors.Right;
        private static readonly Color LeftColor = SwingColors.Left;

        // 向きの印だけ、赤を明るくして青と釣り合わせる。
        // 同じ彩度でも赤は暗く見える（目に見える明るさ 0.299R+0.587G+0.114B が
        // 赤 103 に対し青 117）。細い線なので、その差がそのまま重さの差になる。
        private static readonly Color LeftMarkColor = SwingColors.LeftMark;

        /// <summary>振りとノーツの色（<see cref="SwingColors"/>）。フォアは手の色、バックはずらした色。</summary>
        private static Color SwingColor(int hand, bool forehand)
        {
            return SwingColors.Of(hand, forehand);
        }

        /// <summary>向きの印の色。左手のフォアだけ、赤を明るくした色（細い線で青と釣り合わせる）。</summary>
        private static Color MarkColor(int hand, bool forehand)
        {
            return hand == 0 && forehand ? LeftMarkColor : SwingColor(hand, forehand);
        }

        /// <summary>振りの i 番目の区間（i-1 → i）がフォアか。折り返し点の前後で分ける（<see cref="SwingScene.Swing.SegmentForehand"/>）。</summary>
        private static bool SegmentForehand(Swing swing, int i)
        {
            return swing.SegmentForehand(i);
        }

        /// <summary>振り（Core の <see cref="SwingScene"/>）とノーツ（Core の <see cref="NoteFigures"/>）。MOD の図と同じもの。</summary>
        private SwingScene _scene = new SwingScene();
        private List<Note> _notes = new List<Note>();
        private List<Swing> _swings { get { return _scene.Swings; } }
        private List<Swing> _means { get { return _scene.Means; } }
        private RectangleF _bounds;

        /// <summary>ノーツの置く位置（画面の前の平面、m）。</summary>
        private static PointF CenterOf(Note note) { return new PointF((float)note.CenterX, (float)note.CenterY); }

        public ReplayView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Background;
        }

        /// <summary>どちらの手を描くか。null なら両手。</summary>
        public int? OnlyHand { get; set; }

        /// <summary>平均軌道だけを太く描くか。</summary>
        public bool ShowMeanOnly { get; set; }

        /// <summary>
        /// フォアだけ（true）かバックだけ（false）を出す。両方なら null。
        /// 振りは折り返し点の前後で分ける（色と同じ決め方）。ノーツは切る向きで分ける。
        /// </summary>
        public bool? OnlyForehand { get; set; }

        private bool ShownSide(bool forehand) { return !OnlyForehand.HasValue || OnlyForehand.Value == forehand; }

        /// <summary>
        /// ノーツを出すか。フォアだけ・バックだけのときは、その向きのノーツだけ。
        /// 左右だけ・向き指定なしのノーツは切る向きで決まらないので、どちらでも出す（<see cref="SwingSides.NoteShown"/>）。
        /// </summary>
        private bool NoteShown(Note note) { return SwingSides.NoteShown(note.CutDirection, OnlyForehand); }

        /// <summary>
        /// 正面図に遠近を付けるか。付けると、頭の少し後ろから見た絵になり、手前（手元）は大きく、奥（先端）は小さく描く。
        /// 遠近の無い絵では手元の小さい弧が奥にすぼまって見え、実際の前後と逆に読めてしまうため。
        /// 大きさの基準はノーツを切った深さで、ノーツと切った線は今までどおりの大きさ・位置になる。
        /// </summary>
        public bool Perspective
        {
            get { return _perspective; }
            set
            {
                if (_perspective == value) return;
                _perspective = value;
                ComputeBounds();
                Invalidate();
            }
        }

        private bool _perspective;

        /// <summary>遠近の視点（頭の位置からカメラを少し後ろへ引いたもの）。</summary>
        private Vector3 _eye;

        /// <summary>遠近で大きさを変えない深さ（ノーツを切った深さの平均、m）。</summary>
        private float _referenceZ;

        /// <summary>
        /// 遠近の視点を頭からどれだけ後ろへ引くか (m)。
        /// 振りかぶりで先端が頭より 0.5 m ほど後ろへ回るので、それより十分後ろに置く。
        /// 近すぎると視点のそばの点が何倍にも広がり、図が崩れた（1 m では崩れた）。
        /// </summary>
        private const float CameraBack = 2.0f;

        /// <summary>視点にこれより近い点は、この深さにあるものとして描く (m)。広がりすぎないための下限。</summary>
        private const float NearestDepth = 0.8f;

        /// <summary>ノーツを本体の格子ではなく、実際に切った位置に置くか。</summary>
        public bool AnchorNotesToCuts { get; set; }

        /// <summary>絵の中に大きく出す手ごとの数字。無ければ null。</summary>
        public sealed class HandSummary
        {
            /// <summary>再現。100点満点の点数で、割合ではない。</summary>
            public double Reproducibility;

            /// <summary>1ノーツあたりの平均スコア (0-115)。</summary>
            public double AverageCut;

            /// <summary>要求された向きからの角度ずれ (度)。</summary>
            public double AngleError;

            /// <summary>角度。100点満点。90 度ずれたら 0。</summary>
            public double AnglePercent;

            /// <summary>フォアの振りかぶり／振り抜き。その向きの振りが無ければ null。</summary>
            public SwingAngles Fore;

            /// <summary>バックの振りかぶり／振り抜き。その向きの振りが無ければ null。</summary>
            public SwingAngles Back;
        }

        /// <summary>
        /// 片側の振りかぶりと振り抜き (%)。横から見た図に添える。
        ///
        /// 数字と絵を離して置くと、どちらの線がどの数字なのかを覚えながら見ることになる。
        /// 往路と復路は絵の中で重なって出るので、なおさら突き合わせにくい。
        /// </summary>
        public sealed class SwingAngles
        {
            /// <summary>振りかぶり (%)。<c>PRE</c>。クランプしていないので 100 を超える。</summary>
            public double PreSwing;

            /// <summary>振り抜き (%)。<c>POST</c>。</summary>
            public double PostSwing;
        }

        public HandSummary LeftSummary { get; set; }
        public HandSummary RightSummary { get; set; }

        public int SwingCount { get { return _swings.Count; } }

        /// <param name="replay">軌道に使うリプレイ（タワー・スタックは端の点だけにまとめたもの）。</param>
        /// <param name="notesFrom">ノーツを置くリプレイ（まとめる前）。null なら <paramref name="replay"/>。</param>
        public void Load(Replay replay, Replay notesFrom = null)
        {
            _scene = SwingScene.Build(replay, withNotes: false);
            _notes = replay == null ? new List<Note>() : NoteFigures.Build(notesFrom ?? replay, AnchorNotesToCuts);
            if (replay != null) SetupPerspective(notesFrom ?? replay);

            ComputeBounds();
            Invalidate();
        }

        /// <summary>遠近の視点と、大きさを変えない深さを決める。</summary>
        private void SetupPerspective(Replay replay)
        {
            var head = NoteFigures.HeadAtCuts(replay);
            _eye = new Vector3 { X = head.X, Y = head.Y, Z = head.Z - CameraBack };

            var cuts = replay.Notes.Where(n => n.EventType == NoteEventType.Good && n.Cut != null).ToList();
            _referenceZ = cuts.Count > 0 ? cuts.Average(n => n.Cut.CutPoint.Z) : 0f;
        }

        /// <summary>描画範囲。全ての軌道が入るように取る。</summary>
        private void ComputeBounds()
        {
            if (_swings.Count == 0) { _bounds = new RectangleF(-1, 0, 2, 2); return; }

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var swing in _swings)
                foreach (var p in swing.Tips)
                {
                    var q = Project(p);
                    if (q.X < minX) minX = q.X;
                    if (q.X > maxX) maxX = q.X;
                    if (q.Y < minY) minY = q.Y;
                    if (q.Y > maxY) maxY = q.Y;
                }

            // ノーツが枠の外に出ると位置関係が読めない。
            foreach (var note in _notes)
            {
                // 横から見た図には、切った瞬間のノーツを置く。先端より奥にあることが多いので、範囲に入れる
                if (_axis == ViewAxis.Side)
                {
                    if (!note.HasCutCenter) continue;
                    var side = Project(note.CutCenter);
                    minX = Math.Min(minX, side.X - NoteSize / 2);
                    maxX = Math.Max(maxX, side.X + NoteSize / 2);
                    minY = Math.Min(minY, side.Y - NoteSize / 2);
                    maxY = Math.Max(maxY, side.Y + NoteSize / 2);
                    continue;
                }

                minX = Math.Min(minX, (float)note.CenterX - NoteSize / 2);
                maxX = Math.Max(maxX, (float)note.CenterX + NoteSize / 2);
                minY = Math.Min(minY, (float)note.CenterY - NoteSize / 2);
                maxY = Math.Max(maxY, (float)note.CenterY + NoteSize / 2);
            }

            float padX = Math.Max(0.1f, (maxX - minX) * 0.12f);
            float padY = Math.Max(0.1f, (maxY - minY) * 0.12f);
            _bounds = new RectangleF(minX - padX, minY - padY,
                                     (maxX - minX) + padX * 2, (maxY - minY) + padY * 2);
        }

        /// <summary>
        /// 世界座標を、いま見ている平面へ落とす。縦は<b>どちらの図でも高さ</b>。
        ///
        /// 横から見るときは奥行きを左右に使う。どちら向きに寝かせるかは
        /// <see cref="FaceRight"/>。既定はプレイヤーが右を向いた形（奥が右）。
        /// </summary>
        private PointF Project(Vector3 p)
        {
            if (_axis != ViewAxis.Side)
            {
                if (!_perspective || _referenceZ <= _eye.Z) return new PointF(p.X, p.Y);

                // ノーツを切った深さで 1 倍。手前ほど大きく、奥ほど小さく
                float depth = Math.Max(NearestDepth, p.Z - _eye.Z);
                float scale = (_referenceZ - _eye.Z) / depth;
                return new PointF(_eye.X + (p.X - _eye.X) * scale, _eye.Y + (p.Y - _eye.Y) * scale);
            }

            return new PointF(_faceRight ? p.Z : -p.Z, p.Y);
        }

        /// <summary>
        /// 上の数字に譲る高さ (px)。絵はこのぶん下から描く。
        ///
        /// 0 のまま中央に置くと、束やノーツが数字に乗る。透かして読めるものではないので、
        /// 数字の下を絵の天井として扱う。
        /// </summary>
        private float _topInset;

        /// <summary>平面の座標を画面へ。<see cref="Project"/> を通した値を渡すこと。</summary>
        private PointF ToScreen(PointF p)
        {
            float room = Math.Max(1f, Height - _topInset);

            float scale = Math.Min(Width / _bounds.Width, room / _bounds.Height);
            float offsetX = (Width - _bounds.Width * scale) / 2f;
            float offsetY = (room - _bounds.Height * scale) / 2f;

            // 画面は下が正なので Y を反転する。
            return new PointF(
                offsetX + (p.X - _bounds.X) * scale,
                Height - offsetY - (p.Y - _bounds.Y) * scale);
        }

        /// <summary>
        /// 上の数字が使う高さ。数字を出さないときは 0。
        ///
        /// どちらの図も「見出し1行＋3行」までなので、いちばん大きい字で測って
        /// その高さを譲る。横の図は幅が足りないと字を小さくするが、
        /// 多めに譲るぶんには絵が少し下がるだけで読み違えは起きない。
        /// </summary>
        private float HeaderHeight(Graphics g)
        {
            bool hasNumbers = _axis == ViewAxis.Front
                ? LeftSummary != null || RightSummary != null
                : (LeftSummary != null && (LeftSummary.Fore != null || LeftSummary.Back != null)) ||
                  (RightSummary != null && (RightSummary.Fore != null || RightSummary.Back != null));

            if (!hasNumbers) return 0f;

            using (var value = new Font(Font.FontFamily, _axis == ViewAxis.Front ? 13f : 9.5f, FontStyle.Bold))
                return 8f + (value.GetHeight(g) + 3f) * 3f + 8f;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Background);

            if (_swings.Count == 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(0x70, 0x78, 0x88)))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(Lang.T("軌道がありません", "No swings"), Font, brush, ClientRectangle, format);
                return;
            }

            _topInset = HeaderHeight(g);

            DrawGrid(g);
            if (_axis == ViewAxis.Front) DrawNotes(g, filled: true);

            // 1本1本は薄く。重ねたときの束の太さが再現性そのものになる。
            foreach (var swing in _swings)
            {
                if (ShowMeanOnly) break;
                if (OnlyHand.HasValue && swing.SaberType != OnlyHand.Value) continue;

                // 平均から外れた振りほど目立たせる。
                int alpha = (int)Math.Min(200, 40 + swing.Deviation * 1600);
                DrawSwing(g, swing, c => Color.FromArgb(alpha, c), 1.4f, round: false);
            }

            // 枠と矢印と軸は最後に。束の下に隠れると位置も角度も読めない。
            if (_axis == ViewAxis.Front)
            {
                DrawScores(g);
                DrawAxis(g);
                DrawNotes(g, filled: false);
                DrawCutLines(g);
                DrawSwungDirections(g);
            }
            else
            {
                DrawSideNotes(g);
                DrawSwingAngles(g);
            }

            foreach (var mean in _means)
            {
                if (OnlyHand.HasValue && mean.SaberType != OnlyHand.Value) continue;

                DrawSwing(g, mean, c => Color.FromArgb(255, ControlPaint.Light(c, 0.4f)), 3.2f, round: true);
                DrawDirectionMarks(g, mean.Tips, i => ShownSide(SegmentForehand(mean, i)) ? MarkColor(mean.SaberType, SegmentForehand(mean, i)) : Color.Empty);
            }

            // 切った瞬間の先端。正面図では白い線（ノーツのどこを切ったか）と先端の軌跡がここでつながる
            DrawCutMarks(g);
        }

        /// <summary>ノーツの一辺 (m)。本体の見た目に合わせた大きさ。</summary>
        private const float NoteSize = 0.5f;

        /// <summary>
        /// ノーツを置く。振りの束がどこからどこへ渡っているかが、これで初めて読める。
        ///
        /// 縦位置は身長ぶんの下駄を切った位置から当てはめたものなので、
        /// 数 cm はずれる。位置関係を見るためのもので、寸法を測るものではない。
        /// </summary>
        /// <summary>
        /// ノーツどうしを直線で結んで、その角度を出す。
        ///
        /// ノーツ四角の回転は <c>_cutDirection</c>（8方向しか無い）で決まるので、
        /// 配置がその8方向に乗っていないと、軸とノーツの向きが食い違う。
        /// それを目で確かめられるようにする線。
        /// </summary>
        private void DrawAxis(Graphics g)
        {
            foreach (var hand in new[] { 0, 1 })
            {
                if (OnlyHand.HasValue && hand != OnlyHand.Value) continue;

                var notes = _notes.Where(n => n.ColorType == hand && !n.Shared)
                    .OrderBy(n => n.CenterY)
                    .ToList();
                if (notes.Count < 2) continue;

                Color color = hand == 0 ? LeftColor : RightColor;

                for (int i = 1; i < notes.Count; i++)
                {
                    var from = CenterOf(notes[i - 1]);
                    var to = CenterOf(notes[i]);

                    var a = ToScreen(from);
                    var b = ToScreen(to);

                    using (var pen = new Pen(Color.FromArgb(190, ControlPaint.Light(color, 0.6f)), 1.4f))
                    {
                        pen.DashStyle = DashStyle.Dash;
                        g.DrawLine(pen, a, b);
                    }

                    // 水平からの角度。ノーツの回転（45°刻み）と合っているかを見る。
                    double degrees = Math.Abs(Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI);
                    if (degrees > 90) degrees = 180 - degrees;

                    using (var brush = new SolidBrush(Color.FromArgb(220, ControlPaint.Light(color, 0.6f))))
                        g.DrawString(degrees.ToString("0.0") + "°", Font, brush,
                            (a.X + b.X) / 2f + 6, (a.Y + b.Y) / 2f - 8);
                }
            }
        }

        private void DrawNotes(Graphics g, bool filled)
        {
            // 同じマスにまとめたノーツは、出すもののうち最初の1つが四角と中心の点を描き、ほかは三角だけ。
            // フォアだけ・バックだけで先頭のノーツが外れても、残ったノーツの四角が抜けないように、絞った後で決める
            var drawn = new HashSet<string>();

            foreach (var note in _notes)
            {
                if (OnlyHand.HasValue && note.ColorType != OnlyHand.Value) continue;
                if (!NoteShown(note)) continue;

                Color color = note.ColorType == 0 ? LeftColor : RightColor;

                var center = CenterOf(note);
                var topLeft = ToScreen(new PointF(center.X - NoteSize / 2, center.Y + NoteSize / 2));
                var bottomRight = ToScreen(new PointF(center.X + NoteSize / 2, center.Y - NoteSize / 2));
                var box = RectangleF.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);

                bool headOnly = !drawn.Add(note.ColorType + "," + note.LineIndex + "," + note.LineLayer);
                if (headOnly && filled) continue;
                DrawNote(g, box, color, note.CutDirection, note, filled, headOnly: headOnly);
            }
        }

        /// <summary>
        /// ノーツ1個。本体（と ScoreSaber の成績画面）と同じ見た目にする。
        /// 角丸の四角を切る向きに回し、その縁に白い三角、中心に点。
        /// 向き指定なし（8）は四角のまま中心の点だけ。
        /// </summary>
        /// <summary>
        /// 再現性を絵の中に大きく出す。左手は左上、右手は右上。
        /// 表の小さい数字を目で追わなくても、束の太さと突き合わせられるように。
        /// </summary>
        /// <summary>
        /// 手ごとの数字を3行で、左手は左上・右手は右上に置く。
        ///
        /// 見出し（再現・点数・角度）は数値より小さく、薄く。
        /// 読みたいのは数値の方で、どれがどれかは一度覚えれば済む。
        /// 左右の断りは書かない。色と置き場所で分かる。
        /// </summary>
        /// <summary>数字を置ける大きさ。入らなければ順に小さくする。</summary>
        private static readonly float[] ScoreFontSizes = { 13f, 11.5f, 10f, 9f };

        private void DrawScores(Graphics g)
        {
            // 左手は左、右手は右。幅が足りないと真ん中で重なるので、字の方を小さくする。
            for (int i = 0; i < ScoreFontSizes.Length; i++)
            {
                float size = ScoreFontSizes[i];

                using (var value = new Font(Font.FontFamily, size, FontStyle.Bold))
                using (var label = new Font(Font.FontFamily, size * 0.66f))
                {
                    float left = ScoreWidth(g, value, label, LeftSummary, 0);
                    float right = ScoreWidth(g, value, label, RightSummary, 1);

                    // いちばん小さい字でも入らないなら、それで詰めて出す。重ねるよりは読める。
                    if (left + right + 30f > Width && i < ScoreFontSizes.Length - 1) continue;

                    DrawScore(g, value, label, LeftSummary, 0);
                    DrawScore(g, value, label, RightSummary, 1);
                    return;
                }
            }
        }

        /// <summary>1手ぶんの数字が要る幅。出さない手は 0。</summary>
        private float ScoreWidth(Graphics g, Font value, Font label, HandSummary hand, int saberType)
        {
            if (hand == null) return 0f;
            if (OnlyHand.HasValue && OnlyHand.Value != saberType) return 0f;

            float width = 0f;
            string[] labels, values;
            ScoreLines(hand, out labels, out values);

            for (int i = 0; i < labels.Length; i++)
                width = Math.Max(width,
                    g.MeasureString(labels[i], label).Width + g.MeasureString(values[i], value).Width);

            return width;
        }

        /// <summary>絵の中に出す3行。見出しと数値。</summary>
        private static void ScoreLines(HandSummary hand, out string[] labels, out string[] values)
        {
            var ci = CultureInfo.InvariantCulture;

            labels = new[] { Lang.T("再現", "Repro"), Lang.T("点数", "Score"), Lang.T("角度", "Angle") };
            // 再現も角度も「割合」ではなく目盛りに載せた点数。% と書くと嘘になる。
            values = new[]
            {
                ScoreFormat.Floor(hand.Reproducibility, 1) + " /100",
                ScoreFormat.Floor(hand.AverageCut, 1) + " /115",
                ScoreFormat.Floor(hand.AnglePercent, 1) + " /100",
            };
        }

        private void DrawScore(Graphics g, Font value, Font label, HandSummary hand, int saberType)
        {
            if (hand == null) return;
            if (OnlyHand.HasValue && OnlyHand.Value != saberType) return;

            Color color = saberType == 0 ? LeftColor : RightColor;

            string[] labels, values;
            ScoreLines(hand, out labels, out values);

            float step = value.GetHeight(g) + 3f;
            float top = 8f;

            // 見出しは背が低いので、下端を数値に合わせて置く。
            float drop = value.GetHeight(g) - label.GetHeight(g);

            using (var strong = new SolidBrush(Color.FromArgb(235, ControlPaint.Light(color, 0.4f))))
            using (var faint = new SolidBrush(Color.FromArgb(140, ControlPaint.Light(color, 0.4f))))
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    float y = top + step * i;
                    float labelWidth = g.MeasureString(labels[i], label).Width;
                    float valueWidth = g.MeasureString(values[i], value).Width;

                    float x = saberType == 0 ? 12f : Width - 12f - labelWidth - valueWidth;

                    g.DrawString(labels[i], label, faint, x, y + drop);
                    g.DrawString(values[i], value, strong, x + labelWidth, y);
                }
            }
        }

        /// <summary>
        /// 横から見た図に PRE / POST を添える。<b>フォアとバックを分けて出す。</b>
        ///
        /// この図で見たいのは「打点の面の手前と先にどれだけ伸びているか」で、
        /// それを数字にしたものが PRE / POST。並べて置かないと、
        /// 絵の長さと数字を頭の中で突き合わせることになる。
        /// 往路と復路は絵の中で重なって出るので、まとめた1つの数字では
        /// どちらの伸びが足りないのかが分からない。
        ///
        /// 左手は左上、右手は右上。正面図の数字と同じ置き方にしてある。
        /// </summary>
        /// <summary>数字を置ける大きさ。入らなければ順に小さくする。</summary>
        private static readonly float[] AngleFontSizes = { 9.5f, 8.5f, 7.5f, 6.5f };

        private void DrawSwingAngles(Graphics g)
        {
            // 左手は左、右手は右。<b>縦に積まない。</b>
            // 積むと左右がどちらも上にあることになって、絵の中の赤い束・青い束と
            // 置き場所で結べなくなる（色だけが手がかりになってしまう）。
            // 幅が足りないときは字の方を小さくする。
            for (int i = 0; i < AngleFontSizes.Length; i++)
            {
                float size = AngleFontSizes[i];

                using (var label = new Font(Font.FontFamily, size - 1f))
                using (var value = new Font(Font.FontFamily, size, FontStyle.Bold))
                {
                    var left = BlockOf(g, label, value, LeftSummary, 0);
                    var right = BlockOf(g, label, value, RightSummary, 1);
                    if (left == null && right == null) return;

                    float used = (left == null ? 0f : left.Width) + (right == null ? 0f : right.Width);

                    // いちばん小さい字でも入らないなら、それで詰めて出す。切るよりは読める。
                    if (used + 30f > Width && i < AngleFontSizes.Length - 1) continue;

                    if (left != null) Draw(g, left, 10f, 8f);
                    if (right != null) Draw(g, right, Width - 10f - right.Width, 8f);
                    return;
                }
            }
        }

        /// <summary>1手ぶんの数字のかたまり。置き場所を決めてから描くので、寸法を先に出す。</summary>
        private sealed class AngleBlock
        {
            public Color Color;
            public Font Label;
            public Font Value;
            public string[] Captions;
            public string[] Numbers;
            public float CaptionWidth;
            public float Width;
            public float Height;
            public float Step;
            public float Drop;
        }

        private AngleBlock BlockOf(Graphics g, Font label, Font value, HandSummary hand, int saberType)
        {
            if (hand == null) return null;
            if (OnlyHand.HasValue && OnlyHand.Value != saberType) return null;
            if (hand.Fore == null && hand.Back == null) return null;

            var ci = CultureInfo.InvariantCulture;

            var captions = new List<string> { "PRE / POST %" };
            var numbers = new List<string> { "" };

            if (hand.Fore != null) { captions.Add(Lang.T("フォア", "Fore")); numbers.Add(Percents(hand.Fore, ci)); }
            if (hand.Back != null) { captions.Add(Lang.T("バック", "Back")); numbers.Add(Percents(hand.Back, ci)); }

            // 見出しと数字の間は、いちばん広い見出しで揃える。
            float captionWidth = 0f;
            for (int i = 1; i < captions.Count; i++)
                captionWidth = Math.Max(captionWidth, g.MeasureString(captions[i], label).Width);

            float numberWidth = 0f;
            foreach (var n in numbers) numberWidth = Math.Max(numberWidth, g.MeasureString(n, value).Width);

            float step = value.GetHeight(g) + 2f;

            return new AngleBlock
            {
                Color = saberType == 0 ? LeftColor : RightColor,
                Label = label,
                Value = value,
                Captions = captions.ToArray(),
                Numbers = numbers.ToArray(),
                CaptionWidth = captionWidth,
                Width = Math.Max(captionWidth + 6f + numberWidth, g.MeasureString(captions[0], label).Width),
                Height = step * captions.Count,
                Step = step,
                Drop = value.GetHeight(g) - label.GetHeight(g),
            };
        }

        private static void Draw(Graphics g, AngleBlock block, float left, float top)
        {
            using (var strong = new SolidBrush(Color.FromArgb(235, ControlPaint.Light(block.Color, 0.4f))))
            using (var faint = new SolidBrush(Color.FromArgb(150, ControlPaint.Light(block.Color, 0.4f))))
                for (int i = 0; i < block.Captions.Length; i++)
                {
                    float y = top + block.Step * i;
                    g.DrawString(block.Captions[i], block.Label, faint, left, y + block.Drop);
                    g.DrawString(block.Numbers[i], block.Value, strong, left + block.CaptionWidth + 6f, y);
                }
        }

        /// <summary>「PRE / POST」の1行。その向きの振りが無ければ空。</summary>
        private static string Percents(SwingAngles side, CultureInfo ci)
        {
            if (side == null) return "";

            return ScoreFormat.Floor(side.PreSwing, 0) + " / " + ScoreFormat.Floor(side.PostSwing, 0);
        }

        /// <summary>
        /// 横から見たときの打点。<b>平均軌道の終わりの点</b>に置く。
        ///
        /// 1本の振りは「前のノーツを切った瞬間から次を切る瞬間まで」で切り出してあるので、
        /// 平均軌道の終点が<b>切った瞬間のブレード先端</b>そのものになる。
        /// 印は定義からして束の上に乗る。
        ///
        /// リプレイの <c>cutPoint</c>（ノーツ面を横切った点）は使わない。
        /// 絵に描いているのは<b>先端</b>の道のりで、ノーツを切るのは<b>刃のどこか</b>なので、
        /// その2つは刃の長さぶん（十数 cm）離れる。実際、面の位置に線を引くと
        /// 束から浮いて、何を指している線なのか読めなかった。
        ///
        /// 読み方は、印より<b>手前が振りかぶり (PRE)、先が振り抜き (POST)</b>。
        /// 印から印までの弧が1本の振りで、前の印を出た直後がそのノーツの振り抜き、
        /// 次の印に入る手前が次のノーツの振りかぶりになる。
        /// </summary>
        private void DrawCutMarks(Graphics g)
        {
            foreach (var mean in _means)
            {
                if (OnlyHand.HasValue && mean.SaberType != OnlyHand.Value) continue;
                if (mean.Tips.Length == 0) continue;
                if (!ShownSide(mean.ToForehand)) continue;

                var dot = ToScreen(Project(mean.Tips[mean.Tips.Length - 1]));

                // 束と同じ色だと埋もれる。打点だけ白で抜く。
                using (var brush = new SolidBrush(Color.FromArgb(245, 0xF2, 0xF5, 0xFA)))
                using (var pen = new Pen(Color.FromArgb(255, 0x10, 0x12, 0x16), 1.4f))
                {
                    g.FillEllipse(brush, dot.X - 4f, dot.Y - 4f, 8f, 8f);
                    g.DrawEllipse(pen, dot.X - 4f, dot.Y - 4f, 8f, 8f);
                }
            }
        }

        /// <summary>
        /// ノーツのどこを切ったか。1回ごとに切り口の線を、ノーツの幅ぶんの長さで薄く重ねる。
        /// 中心の点から線までの距離が、点数の中心の 15 点に効くずれ（cutDistanceToCenter）。
        /// </summary>
        private void DrawCutLines(Graphics g)
        {
            using (var pen = new Pen(Color.FromArgb(80, 0xF2, 0xF5, 0xFA), 1.2f))
            using (var arrow = new AdjustableArrowCap(3.2f, 3.6f, true))
            using (var meanPen = new Pen(Color.FromArgb(240, 0xF2, 0xF5, 0xFA), 2.4f) { StartCap = LineCap.Round })
            {
                // 線の先が振った向き（振り抜いた側）
                meanPen.CustomEndCap = arrow;

                foreach (var note in _notes)
                {
                    if (OnlyHand.HasValue && note.ColorType != OnlyHand.Value) continue;
                    if (!NoteShown(note)) continue;
                    if (note.Cuts.Count == 0) continue;

                    // 面の座標 (u, v) → 正面の平面。面の右・上を X-Y へ落としたもので、四角と同じだけ縮む
                    Func<float, float, PointF> onFace = (u, v) => ToScreen(new PointF(
                        (float)(note.CenterX + note.RightX * u + note.UpX * v),
                        (float)(note.CenterY + note.RightY * u + note.UpY * v)));

                    // 平均だけのときは、向きと横のずれを平均した1本にする
                    var lines = ShowMeanOnly ? new List<double[][]> { NoteFigures.MeanCut(note.Cuts) } : note.Cuts;
                    var drawWith = ShowMeanOnly ? meanPen : pen;

                    foreach (var cut in lines)
                        g.DrawLines(drawWith, cut.Select(p => onFace((float)p[0], (float)p[1])).ToArray());
                }
            }
        }

        /// <summary>
        /// 横から見た図のノーツ。切った瞬間の中心（平均）に、一辺 <see cref="NoteSize"/> の四角を置く。
        /// 向き（上下）ごとに別の四角で、中に切る向きの小さな三角。
        /// 先端の印（白丸）が四角の手前にあれば、刃の先でノーツの手前側をかすめて切っている。
        /// </summary>
        private void DrawSideNotes(Graphics g)
        {
            foreach (var note in _notes)
            {
                if (!note.HasCutCenter) continue;
                if (OnlyHand.HasValue && note.ColorType != OnlyHand.Value) continue;
                if (!NoteShown(note)) continue;

                Color color = SwingColor(note.ColorType, note.Forehand);
                var c = note.CutCenter;
                float h = NoteSize / 2;
                var a = ToScreen(Project(new Vector3 { X = c.X, Y = c.Y + h, Z = c.Z - h }));
                var b = ToScreen(Project(new Vector3 { X = c.X, Y = c.Y - h, Z = c.Z + h }));
                var box = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

                using (var path = RoundedSquare(box.Width, box.Width * 0.2f))
                {
                    var saved = g.Save();
                    g.TranslateTransform(box.X + box.Width / 2, box.Y + box.Height / 2);
                    using (var fill = new SolidBrush(Color.FromArgb(60, color)))
                        g.FillPath(fill, path);
                    using (var edge = new Pen(Color.FromArgb(220, ControlPaint.Light(color, 0.35f)), 1.8f))
                        g.DrawPath(edge, path);

                    // 切る向きの上下だけ。横から見ると左右は奥行きに潰れる
                    var arrow = ArrowDirection(note.CutDirection);
                    if (arrow.HasValue && arrow.Value.Y != 0)
                    {
                        float s = box.Width * 0.18f;
                        float dir = arrow.Value.Y;   // 画面は下が正
                        using (var head = new SolidBrush(Color.FromArgb(200, 0xF2, 0xF5, 0xFA)))
                            g.FillPolygon(head, new[]
                            {
                                new PointF(0f, dir * s),
                                new PointF(-s, -dir * s * 0.6f),
                                new PointF(s, -dir * s * 0.6f),
                            });
                    }
                    g.Restore(saved);
                }
            }
        }

        /// <summary>実際に振った向きを示す線の長さ (m)。</summary>
        private const float SwungMarkLength = 0.34f;

        /// <summary>
        /// ノーツごとに、実際に振った向きを線で出す。
        /// ノーツの矢印（要求された向き）との開きが、そのまま角度ずれになる。
        /// </summary>
        private void DrawSwungDirections(Graphics g)
        {
            using (var text = new Font(Font.FontFamily, 8.5f))
            foreach (var note in _notes)
            {
                if (OnlyHand.HasValue && note.ColorType != OnlyHand.Value) continue;
                if (!NoteShown(note)) continue;
                if (note.SwungX == 0 && note.SwungY == 0) continue;

                var from = ToScreen(CenterOf(note));
                var to = ToScreen(new PointF(
                    (float)(note.CenterX + note.SwungX * SwungMarkLength),
                    (float)(note.CenterY + note.SwungY * SwungMarkLength)));

                using (var pen = new Pen(Color.FromArgb(230, 0xF2, 0xF5, 0xFA), 2f))
                {
                    pen.EndCap = LineCap.ArrowAnchor;
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawLine(pen, from, to);
                }

                using (var brush = new SolidBrush(Color.FromArgb(220, 0xF2, 0xF5, 0xFA)))
                    g.DrawString(note.AngleError.ToString("0.0", CultureInfo.InvariantCulture) + "°",
                        text, brush, to.X + 3f, to.Y - 6f);
            }
        }

        /// <summary>
        /// 平均軌道に「く」の字を並べて向きを示す。
        /// 往路と復路は別の平均として重なって出るので、印が無いとどちらがどちらか読めない。
        /// </summary>
        /// <param name="colorAt">その区間（index-1 → index）の印の色。</param>
        private void DrawDirectionMarks(Graphics g, Vector3[] path, Func<int, Color> colorAt)
        {
            var points = new PointF[path.Length];
            for (int i = 0; i < path.Length; i++) points[i] = ToScreen(Project(path[i]));

            foreach (double at in new[] { 0.25, 0.5, 0.75 })
            {
                int index = Math.Max(1, (int)(at * (points.Length - 1)));
                var markColor = colorAt(index);
                if (markColor.IsEmpty) continue;   // フォアだけ・バックだけで出さない区間

                // 手の色。平均軌道は淡いので、濃い方を印に使うと乗っても読める。
                using (var pen = new Pen(Color.FromArgb(255, markColor), 2.4f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;

                    float dx = points[index].X - points[index - 1].X;
                    float dy = points[index].Y - points[index - 1].Y;
                    float length = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (length < 0.5f) continue;

                    dx /= length;
                    dy /= length;

                    // 進む向きに開いた「く」。
                    const float Arm = 7f;
                    float px = -dy, py = dx;
                    float tipX = points[index].X, tipY = points[index].Y;

                    g.DrawLine(pen, tipX - dx * Arm + px * Arm * 0.75f, tipY - dy * Arm + py * Arm * 0.75f, tipX, tipY);
                    g.DrawLine(pen, tipX - dx * Arm - px * Arm * 0.75f, tipY - dy * Arm - py * Arm * 0.75f, tipX, tipY);
                }
            }
        }

        private static void DrawNote(Graphics g, RectangleF box, Color color, int cutDirection, Note note, bool filled, bool headOnly = false)
        {
            float size = Math.Min(box.Width, box.Height);
            var arrow = ArrowDirection(cutDirection);

            var saved = g.Save();
            g.TranslateTransform(box.X + box.Width / 2, box.Y + box.Height / 2);

            // プレイヤーを向いた面の軸をそのまま使う。斜めを向いたぶん縮んで見える。
            if (arrow.HasValue && (note.RightX != 0 || note.RightY != 0))
            {
                // 局所座標は画素で、そのまま面の軸に載せ替える（画面は下が正なので Y を反転）。
                // 単位ベクトルなので倍率は要らない。正面を向いていなければ短くなる＝縮んで見える。
                using (var axes = new Matrix(
                    (float)note.RightX, (float)-note.RightY,
                    (float)-note.UpX, (float)note.UpY,
                    0f, 0f))
                    g.MultiplyTransform(axes);
            }
            else if (arrow.HasValue)
            {
                g.RotateTransform((float)(note.Tilt ?? 0.0));
            }

            using (var body = RoundedSquare(size, size * 0.2f))
            {
                if (filled)
                {
                    using (var fill = new SolidBrush(Color.FromArgb(120, color)))
                        g.FillPath(fill, body);
                }
                else
                {
                    var bright = ControlPaint.Light(color, 0.35f);

                    if (!headOnly)
                        using (var edge = new Pen(Color.FromArgb(235, bright), Math.Max(1.6f, size * 0.05f)))
                            g.DrawPath(edge, body);

                    // 三角は切る向きと反対側に置き、先が切る向きを指す（BeatLeader と同じ）。
                    // 正三角形ではなく、幅の広い浅い形。
                    if (arrow.HasValue)
                        using (var head = new SolidBrush(Color.FromArgb(150, 0xF2, 0xF5, 0xFA)))
                            g.FillPolygon(head, new[]
                            {
                                new PointF(0f, size * 0.12f),
                                new PointF(-size * 0.34f, size * 0.40f),
                                new PointF(size * 0.34f, size * 0.40f),
                            });

                    // 点はノーツの中心。当たり所を測る基準になるので、位置をずらさない。
                    if (!headOnly)
                    {
                        using (var mark = new SolidBrush(Color.FromArgb(235, 0xF2, 0xF5, 0xFA)))
                        {
                            float dot = size * 0.065f;
                            g.FillEllipse(mark, -dot, -dot, dot * 2, dot * 2);
                        }
                    }
                }
            }

            g.Restore(saved);
        }

        /// <summary>矢印の向き。画面は下が正（<see cref="SwingSides.ArrowOf"/> の上下を返したもの）。向き指定なしは null。</summary>
        private static PointF? ArrowDirection(int cutDirection)
        {
            double x, y;
            if (!SwingSides.ArrowOf(cutDirection, out x, out y)) return null;
            return new PointF((float)x, (float)-y);
        }

        /// <summary>原点中心の角丸四角。</summary>
        private static GraphicsPath RoundedSquare(float size, float radius)
        {
            float half = size / 2f;
            float d = radius * 2f;
            var path = new GraphicsPath();

            path.AddArc(-half, -half, d, d, 180, 90);
            path.AddArc(half - d, -half, d, d, 270, 90);
            path.AddArc(half - d, half - d, d, d, 0, 90);
            path.AddArc(-half, half - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }

        /// <summary>振りを区間ごとに、フォア・バックの色で描く（<see cref="SegmentForehand"/>）。</summary>
        private void DrawSwing(Graphics g, Swing swing, Func<Color, Color> style, float width, bool round)
        {
            var points = new PointF[swing.Tips.Length];
            for (int i = 0; i < points.Length; i++) points[i] = ToScreen(Project(swing.Tips[i]));

            // 同じ色が続く所はまとめて1本の折れ線にする（区間ごとに引くと継ぎ目が欠ける）
            int start = 0;
            for (int i = 1; i < points.Length; i++)
            {
                bool fore = SegmentForehand(swing, i);
                bool last = i == points.Length - 1;
                if (!last && SegmentForehand(swing, i + 1) == fore) continue;

                // フォアだけ・バックだけで出さない部分は飛ばす
                if (!ShownSide(fore)) { start = i; continue; }

                using (var pen = new Pen(style(SwingColor(swing.SaberType, fore)), width))
                {
                    if (round) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; }
                    pen.LineJoin = LineJoin.Round;
                    var run = new PointF[i - start + 1];
                    Array.Copy(points, start, run, 0, run.Length);
                    g.DrawLines(pen, run);
                }
                start = i;
            }
        }

        private void DrawPath(Graphics g, Pen pen, Vector3[] path)
        {
            var points = new PointF[path.Length];
            for (int i = 0; i < path.Length; i++) points[i] = ToScreen(Project(path[i]));
            g.DrawLines(pen, points);
        }

        /// <summary>
        /// 目盛り代わりの 10cm 格子。ずれの大きさを目で測れるように。
        ///
        /// 説明の字は入れない。図が縦に長いと下に横1行ぶんの余地が無く、
        /// 入れると必ず途中で切れる。目盛りの刻みは README に書いてある。
        /// </summary>
        private void DrawGrid(Graphics g)
        {
            using (var pen = new Pen(GridColor, 1f))
            {
                for (double x = Math.Ceiling(_bounds.Left * 10) / 10.0; x < _bounds.Right; x += 0.1)
                {
                    var a = ToScreen(new PointF((float)x, _bounds.Top));
                    var b = ToScreen(new PointF((float)x, _bounds.Bottom));
                    g.DrawLine(pen, a, b);
                }

                for (double y = Math.Ceiling(_bounds.Top * 10) / 10.0; y < _bounds.Bottom; y += 0.1)
                {
                    var a = ToScreen(new PointF(_bounds.Left, (float)y));
                    var b = ToScreen(new PointF(_bounds.Right, (float)y));
                    g.DrawLine(pen, a, b);
                }
            }
        }
    }
}
