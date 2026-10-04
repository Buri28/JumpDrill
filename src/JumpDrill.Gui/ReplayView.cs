using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using JumpDrill.Replays;

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
        private static readonly Color RightColor = Color.FromArgb(0x2E, 0x86, 0xD8);
        private static readonly Color LeftColor = Color.FromArgb(0xD0, 0x3A, 0x3A);

        // 向きの印だけ、赤を明るくして青と釣り合わせる。
        // 同じ彩度でも赤は暗く見える（目に見える明るさ 0.299R+0.587G+0.114B が
        // 赤 103 に対し青 117）。細い線なので、その差がそのまま重さの差になる。
        private static readonly Color LeftMarkColor = Color.FromArgb(0xE6, 0x55, 0x55);

        private readonly List<Swing> _swings = new List<Swing>();
        private readonly List<Note> _notes = new List<Note>();
        private RectangleF _bounds;
        /// <summary>格子を当てはめたときの残差 (m)。<c>NoteGrid.FitTo</c> が返すだけで、絵には出さない。</summary>
        private double _gridResidual;

        /// <summary>描くノーツ。位置は本体の格子、置き場所はリプレイから当てはめたもの。</summary>
        private sealed class Note
        {
            public int ColorType;
            public PointF Center;

            /// <summary>本体の格子上の位置。打点に寄せても変わらない。</summary>
            public PointF GridCenter;

            /// <summary>
            /// 傾きを決めるための位置。<b>飛距離の半分の地点の高さ</b>（縦も 0.6 等間隔）。
            ///
            /// 本体の角度オフセットは <c>Get2DNoteOffset</c> の差で向きを決めていて、
            /// そこが使うのは <c>LineYPosForLineLayer</c>（0.25/0.85/1.45）。
            /// 到達時の高さ（0.85/1.40/1.90）ではないので、傾きだけ別の位置で測る。
            /// </summary>
            public PointF TiltCenter;

            public int CutDirection;

            /// <summary>ノーツの傾き（画面上、時計回りの度）。向き指定なしなら null。</summary>
            public float? Tilt;

            /// <summary>切った瞬間の奥行き。プレイヤーの方を向く量がこれで決まる。</summary>
            public float Depth;


            /// <summary>この配置のノーツが出てくる時刻 (秒)。角度オフセットの相手探しに使う。</summary>
            public readonly List<float> Times = new List<float>();

            /// <summary>実際に振った向きの平均（世界座標、X-Y）。振っていなければゼロ。</summary>
            public PointF Swung;

            /// <summary>要求された向きからの角度ずれの平均 (度)。</summary>
            public double AngleError;

            /// <summary>面の右向き・上向きを X-Y へ落としたもの（プレイヤーを向いた後）。</summary>
            public PointF Right;
            public PointF Up;
        }

        private sealed class Swing
        {
            public int SaberType;
            public Vector3[] Path;

            /// <summary>その手の平均軌道からのずれ (m)。色の濃さに使う。</summary>
            public double Deviation;
        }

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

        public void Load(Replay replay)
        {
            _swings.Clear();
            _means.Clear();        // 消し忘れると前に選んだ譜面の平均軌道が残る
            _notes.Clear();

            if (replay != null)
            {
                foreach (int saberType in new[] { 0, 1 })
                    LoadHand(replay, saberType);

                LoadNotes(replay);
            }

            ComputeBounds();
            Invalidate();
        }

        /// <summary>ノーツの置き場所を集める。</summary>
        private void LoadNotes(Replay replay)
        {
            var grid = NoteGrid.FitTo(replay, out _gridResidual);
            var cuts = NoteGrid.CutCenters(replay);

            foreach (var placement in NoteGrid.PlacementsOf(replay))
            {
                var gridCenter = new PointF(grid.X(placement.LineIndex), grid.Y(placement.LineLayer));
                var tiltCenter = new PointF(grid.X(placement.LineIndex), NoteGrid.MidJumpY(placement.LineLayer));

                Vector3 cut;
                var center = AnchorNotesToCuts && cuts.TryGetValue(placement, out cut)
                    ? new PointF(cut.X, cut.Y)
                    : gridCenter;

                Vector3 measured;
                bool hasCut = cuts.TryGetValue(placement, out measured);

                PointF swung;
                double angleError;
                MeasureSwing(replay, placement, out swung, out angleError);

                var note = new Note
                {
                    ColorType = placement.ColorType,
                    Center = center,
                    GridCenter = gridCenter,
                    TiltCenter = tiltCenter,
                    CutDirection = placement.CutDirection,
                    Depth = hasCut ? measured.Z : 0f,
                    Swung = swung,
                    AngleError = angleError,
                };

                foreach (var event_ in replay.Notes)
                    if (event_.ColorType == placement.ColorType &&
                        event_.LineIndex == placement.LineIndex &&
                        event_.LineLayer == placement.LineLayer &&
                        event_.CutDirection == placement.CutDirection)
                        note.Times.Add(event_.EventTime);

                _notes.Add(note);
            }

            ApplyAngleOffsets();
            ApplyLookAtPlayer(replay);
        }

        /// <summary>
        /// その配置を実際にどの向きに振ったか（平均）と、要求された向きからのずれ。
        ///
        /// 向きは <c>saberDir</c> を長さ 1 に直してから平均する。速さで重みが付くと、
        /// 強く振った回だけの向きになってしまう。
        /// ずれの角度は本体が出している <c>cutDirDeviation</c> をそのまま使う。
        /// こちらで測り直すと、ノーツの角度オフセットを含められない。
        /// </summary>
        private static void MeasureSwing(Replay replay, NotePlacement placement,
                                         out PointF direction, out double angleError)
        {
            direction = new PointF();
            angleError = 0.0;

            double x = 0, y = 0, error = 0;
            int count = 0;

            foreach (var note in replay.Notes)
            {
                if (note.EventType != NoteEventType.Good || note.Cut == null) continue;
                if (note.ColorType != placement.ColorType) continue;
                if (note.LineIndex != placement.LineIndex || note.LineLayer != placement.LineLayer) continue;

                var d = note.Cut.SaberDirection;
                double length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                if (length < 1e-6) continue;

                x += d.X / length;
                y += d.Y / length;
                error += Math.Abs(note.Cut.CutDirDeviation);
                count++;
            }

            if (count == 0) return;

            double norm = Math.Sqrt(x * x + y * y);
            if (norm < 1e-6) return;

            direction = new PointF((float)(x / norm), (float)(y / norm));
            angleError = error / count;
        }

        /// <summary>
        /// ノーツをプレイヤーの方へ向ける（本体の <c>NoteJump::_rotateTowardsPlayer</c>）。
        ///
        /// 面が正面を向いていないと、矢印の見かけの角度が変わる。
        /// 外側の列ほど大きく回るので、内側から外へ振る方が面に正対しやすくなる。
        /// </summary>
        private void ApplyLookAtPlayer(Replay replay)
        {
            var head = HeadAtCuts(replay);

            foreach (var note in _notes)
            {
                // 回る前の「上」＝矢印の向き。傾きから戻す。
                double radians = (note.Tilt ?? 0f) * Math.PI / 180.0;
                var baseUp = new Vector3 { X = (float)Math.Sin(radians), Y = (float)Math.Cos(radians), Z = 0f };

                var center = new Vector3 { X = note.Center.X, Y = note.Center.Y, Z = note.Depth };

                Vector3 right, up;
                NotePose.LookAtPlayer(center, head, baseUp, out right, out up);

                note.Right = new PointF(right.X, right.Y);
                note.Up = new PointF(up.X, up.Y);
            }
        }

        /// <summary>打点の瞬間の頭の位置（平均）。</summary>
        private static Vector3 HeadAtCuts(Replay replay)
        {
            var cuts = replay.Notes
                .Where(n => n.EventType == NoteEventType.Good && n.Cut != null)
                .OrderBy(n => n.EventTime)
                .ToList();

            if (cuts.Count == 0 || replay.Frames.Count == 0)
                return new Vector3 { X = 0f, Y = 1.5f, Z = 0f };

            double x = 0, y = 0, z = 0;
            int cursor = 0, count = 0;

            foreach (var note in cuts)
            {
                while (cursor < replay.Frames.Count - 1 && replay.Frames[cursor].Time < note.EventTime) cursor++;

                var position = replay.Frames[cursor].Head.Position;
                x += position.X; y += position.Y; z += position.Z; count++;
            }

            return new Vector3 { X = (float)(x / count), Y = (float)(y / count), Z = (float)(z / count) };
        }

        /// <summary>
        /// 本体の角度オフセットを当てる。
        ///
        /// <c>BeatmapObjectsInTimeRowProcessor</c> は同じ色のノーツ対を、
        /// <c>Get2DNoteOffset</c> の差（＝結ぶ線）と <c>Direction(cutDirection)</c> の
        /// <c>SignedAngleToLine</c> を取り、それを <c>SetCutDirectionAngleOffset</c> で足す。
        /// つまり<b>ノーツは相手との線に合わせて傾く</b>。
        ///
        /// ただし<b>同じ拍に並んだ対にしか当たらない</b>。あちらは1拍ぶんを切り出して、
        /// その中に同じ色が2個あるときだけ角度を足している。
        /// ドリルは片手が1拍に1個ずつなので、ふつうは<b>どのノーツにも当たらない</b>
        /// ＝矢印の向きそのままで立つ。
        ///
        /// 時刻を見ずに配置だけで対を作ると、往復の2点が常に対になってしまい、
        /// <c>s</c>（上下左右に倒す指定）を付けた譜面で、水平に立つはずのノーツが
        /// 移動の軸（例: 1列×2段 = 63.4°）に寝てしまう。譜面の見た目と食い違う。
        ///
        /// 本体はずれが大きすぎると当てない。その境目が 40°。
        /// </summary>
        private void ApplyAngleOffsets()
        {
            const double Limit = 40.0;

            foreach (var hand in new[] { 0, 1 })
            {
                var notes = _notes.Where(n => n.ColorType == hand).ToList();

                foreach (var note in notes)
                {
                    var arrow = ArrowDirection(note.CutDirection);
                    if (!arrow.HasValue) continue;

                    note.Tilt = (float)ToDegrees(arrow.Value);

                    // 相手は、同じ拍に出ていて、かつ矢印が向いている先と<b>逆</b>にいる方
                    // （振り抜く先には居ない）。
                    var partner = notes
                        .Where(other => other != note && SameRow(note, other))
                        .OrderByDescending(other => Dot(arrow.Value, Toward(note, other)))
                        .FirstOrDefault();
                    if (partner == null) continue;

                    var line = Toward(partner, note);
                    if (Math.Abs(line.X) < 1e-6 && Math.Abs(line.Y) < 1e-6) continue;

                    // 相手との線には向きが無い（本体の SignedAngleToLine と同じ）。
                    // 線の2つの向きのうち矢印に近い方へ合わせる。片方の向きだけで測ると、
                    // 矢印が相手の方を向いているノーツでは差が 180° 近くになり、傾かない。
                    double aligned = ToDegrees(line);
                    double difference = Wrap(aligned - note.Tilt.Value);
                    if (Math.Abs(difference) > 90)
                    {
                        aligned = ToDegrees(new PointF(-line.X, -line.Y));
                        difference = Wrap(aligned - note.Tilt.Value);
                    }

                    if (Math.Abs(difference) <= Limit) note.Tilt = (float)aligned;
                }
            }
        }

        /// <summary>
        /// from から to へ向かう向き（画面座標なので Y は反転）。
        /// 本体が角度オフセットを出すときと同じ、素の格子の位置で測る。
        /// </summary>
        /// <summary>同じ拍に並んでいるか。本体が1拍ぶんを切り出すのに合わせた幅。</summary>
        private static bool SameRow(Note a, Note b)
        {
            const float Window = 0.01f;

            foreach (var one in a.Times)
                foreach (var other in b.Times)
                    if (Math.Abs(one - other) <= Window) return true;

            return false;
        }

        private static PointF Toward(Note from, Note to)
        {
            return new PointF(to.TiltCenter.X - from.TiltCenter.X, -(to.TiltCenter.Y - from.TiltCenter.Y));
        }

        /// <summary>角度を -180〜180 に収める。</summary>
        private static double Wrap(double degrees)
        {
            while (degrees > 180) degrees -= 360;
            while (degrees < -180) degrees += 360;
            return degrees;
        }

        private static double Dot(PointF a, PointF b)
        {
            double length = Math.Sqrt(b.X * b.X + b.Y * b.Y);
            return length < 1e-9 ? 0 : (a.X * b.X + a.Y * b.Y) / length;
        }

        /// <summary>局所の上向き (0,-1) をこの向きへ回す角度（度）。</summary>
        private static double ToDegrees(PointF direction)
        {
            return Math.Atan2(direction.X, -direction.Y) * 180.0 / Math.PI;
        }

        private void LoadHand(Replay replay, int saberType)
        {
            var cuts = replay.Notes
                .Where(n => n.EventType == NoteEventType.Good && n.Cut != null && n.Cut.SaberType == saberType)
                .OrderBy(n => n.EventTime)
                .ToList();

            if (cuts.Count < 2) return;

            // 遷移ごとに束ねる。往路と復路を混ぜると別物が重なる。
            var groups = new Dictionary<string, List<Vector3[]>>();

            // 振りは数字と同じ決め方（SwingAnalyzer.CleanPairs）。間でミスしたところは前後をつながない
            foreach (var pair in SwingAnalyzer.CleanPairs(SwingAnalyzer.HandSequence(replay, saberType), saberType))
            {
                var from = pair.Key;
                var to = pair.Value;

                var path = ExtractPath(replay, saberType, from.EventTime, to.EventTime);
                if (path == null) continue;

                string key = from.LineIndex + "," + from.LineLayer + ">" +
                             to.LineIndex + "," + to.LineLayer;

                List<Vector3[]> list;
                if (!groups.TryGetValue(key, out list)) groups[key] = list = new List<Vector3[]>();
                list.Add(path);
            }

            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;

                var mean = Mean(group);
                foreach (var path in group)
                {
                    _swings.Add(new Swing
                    {
                        SaberType = saberType,
                        Path = path,
                        Deviation = MeanDistance(path, mean),
                    });
                }

                // 平均軌道は最後に描きたいので、目印として先頭要素を複製せず別枠で持つ。
                _means.Add(new Swing { SaberType = saberType, Path = mean, Deviation = 0.0 });
            }
        }

        private readonly List<Swing> _means = new List<Swing>();

        private static Vector3[] ExtractPath(Replay replay, int saberType, float from, float to)
        {
            var points = new List<Vector3>();
            foreach (var frame in replay.Frames)
            {
                if (frame.Time < from) continue;
                if (frame.Time > to) break;
                points.Add(frame.SaberTip(saberType));
            }

            if (points.Count < 4) return null;
            return SwingAnalyzer.ResampleByArcLength(points, SwingAnalyzer.SamplesPerSwing);
        }

        private static Vector3[] Mean(List<Vector3[]> paths)
        {
            int samples = paths[0].Length;
            var mean = new Vector3[samples];

            for (int i = 0; i < samples; i++)
            {
                double x = 0, y = 0, z = 0;
                foreach (var p in paths) { x += p[i].X; y += p[i].Y; z += p[i].Z; }
                mean[i] = new Vector3
                {
                    X = (float)(x / paths.Count),
                    Y = (float)(y / paths.Count),
                    Z = (float)(z / paths.Count),
                };
            }
            return mean;
        }

        private static double MeanDistance(Vector3[] path, Vector3[] mean)
        {
            double sum = 0;
            for (int i = 0; i < path.Length; i++) sum += Vector3.Distance(path[i], mean[i]);
            return sum / path.Length;
        }

        /// <summary>描画範囲。全ての軌道が入るように取る。</summary>
        private void ComputeBounds()
        {
            if (_swings.Count == 0) { _bounds = new RectangleF(-1, 0, 2, 2); return; }

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var swing in _swings)
                foreach (var p in swing.Path)
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
                // 横から見た図の印は軌道の終点なので、束より外へ出ることはない。
                // ノーツ1個ぶんの余白を足すと、その分だけ絵が縮むだけになる。
                if (_axis == ViewAxis.Side) continue;

                minX = Math.Min(minX, note.Center.X - NoteSize / 2);
                maxX = Math.Max(maxX, note.Center.X + NoteSize / 2);
                minY = Math.Min(minY, note.Center.Y - NoteSize / 2);
                maxY = Math.Max(maxY, note.Center.Y + NoteSize / 2);
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
            if (_axis != ViewAxis.Side) return new PointF(p.X, p.Y);

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

                Color baseColor = swing.SaberType == 0 ? LeftColor : RightColor;

                // 平均から外れた振りほど目立たせる。
                int alpha = (int)Math.Min(200, 40 + swing.Deviation * 1600);
                using (var pen = new Pen(Color.FromArgb(alpha, baseColor), 1.4f))
                    DrawPath(g, pen, swing.Path);
            }

            // 枠と矢印と軸は最後に。束の下に隠れると位置も角度も読めない。
            if (_axis == ViewAxis.Front)
            {
                DrawScores(g);
                DrawAxis(g);
                DrawNotes(g, filled: false);
                DrawSwungDirections(g);
            }
            else
            {
                DrawCutMarks(g);
                DrawSwingAngles(g);
            }

            foreach (var mean in _means)
            {
                if (OnlyHand.HasValue && mean.SaberType != OnlyHand.Value) continue;

                Color color = mean.SaberType == 0 ? LeftColor : RightColor;
                using (var pen = new Pen(Color.FromArgb(255, ControlPaint.Light(color, 0.4f)), 3.2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    DrawPath(g, pen, mean.Path);
                }

                DrawDirectionMarks(g, mean.Path, mean.SaberType == 0 ? LeftMarkColor : RightColor);
            }
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

                var notes = _notes.Where(n => n.ColorType == hand)
                    .OrderBy(n => n.Center.Y)
                    .ToList();
                if (notes.Count < 2) continue;

                Color color = hand == 0 ? LeftColor : RightColor;

                for (int i = 1; i < notes.Count; i++)
                {
                    var from = notes[i - 1].Center;
                    var to = notes[i].Center;

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
            foreach (var note in _notes)
            {
                if (OnlyHand.HasValue && note.ColorType != OnlyHand.Value) continue;

                Color color = note.ColorType == 0 ? LeftColor : RightColor;

                var topLeft = ToScreen(new PointF(note.Center.X - NoteSize / 2, note.Center.Y + NoteSize / 2));
                var bottomRight = ToScreen(new PointF(note.Center.X + NoteSize / 2, note.Center.Y - NoteSize / 2));
                var box = RectangleF.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);

                DrawNote(g, box, color, note.CutDirection, note, filled);
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
                hand.Reproducibility.ToString("0.0", ci) + " /100",
                hand.AverageCut.ToString("0.0", ci) + " /115",
                hand.AnglePercent.ToString("0.0", ci) + " /100",
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

            return side.PreSwing.ToString("0", ci) + " / " + side.PostSwing.ToString("0", ci);
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
                if (mean.Path.Length == 0) continue;

                var dot = ToScreen(Project(mean.Path[mean.Path.Length - 1]));

                // 束と同じ色だと埋もれる。打点だけ白で抜く。
                using (var brush = new SolidBrush(Color.FromArgb(245, 0xF2, 0xF5, 0xFA)))
                using (var pen = new Pen(Color.FromArgb(255, 0x10, 0x12, 0x16), 1.4f))
                {
                    g.FillEllipse(brush, dot.X - 4f, dot.Y - 4f, 8f, 8f);
                    g.DrawEllipse(pen, dot.X - 4f, dot.Y - 4f, 8f, 8f);
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
                if (note.Swung.X == 0 && note.Swung.Y == 0) continue;

                var from = ToScreen(note.Center);
                var to = ToScreen(new PointF(
                    note.Center.X + note.Swung.X * SwungMarkLength,
                    note.Center.Y + note.Swung.Y * SwungMarkLength));

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
        private void DrawDirectionMarks(Graphics g, Vector3[] path, Color color)
        {
            var points = new PointF[path.Length];
            for (int i = 0; i < path.Length; i++) points[i] = ToScreen(Project(path[i]));

            // 手の色。平均軌道は淡いので、濃い方を印に使うと乗っても読める。
            using (var pen = new Pen(Color.FromArgb(255, color), 2.4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                foreach (double at in new[] { 0.25, 0.5, 0.75 })
                {
                    int index = Math.Max(1, (int)(at * (points.Length - 1)));

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

        private static void DrawNote(Graphics g, RectangleF box, Color color, int cutDirection, Note note, bool filled)
        {
            float size = Math.Min(box.Width, box.Height);
            var arrow = ArrowDirection(cutDirection);

            var saved = g.Save();
            g.TranslateTransform(box.X + box.Width / 2, box.Y + box.Height / 2);

            // プレイヤーを向いた面の軸をそのまま使う。斜めを向いたぶん縮んで見える。
            if (arrow.HasValue && (note.Right.X != 0 || note.Right.Y != 0))
            {
                // 局所座標は画素で、そのまま面の軸に載せ替える（画面は下が正なので Y を反転）。
                // 単位ベクトルなので倍率は要らない。正面を向いていなければ短くなる＝縮んで見える。
                g.MultiplyTransform(new Matrix(
                    note.Right.X, -note.Right.Y,
                    -note.Up.X, note.Up.Y,
                    0f, 0f));
            }
            else if (arrow.HasValue)
            {
                g.RotateTransform(note.Tilt ?? (float)ToDegrees(arrow.Value));
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
                    using (var mark = new SolidBrush(Color.FromArgb(235, 0xF2, 0xF5, 0xFA)))
                    {
                        float dot = size * 0.065f;
                        g.FillEllipse(mark, -dot, -dot, dot * 2, dot * 2);
                    }
                }
            }

            g.Restore(saved);
        }

        /// <summary>矢印の向き。画面は下が正。向き指定なし（8）は null。</summary>
        private static PointF? ArrowDirection(int cutDirection)
        {
            switch (cutDirection)
            {
                case 0: return new PointF(0, -1);    // Up
                case 1: return new PointF(0, 1);     // Down
                case 2: return new PointF(-1, 0);    // Left
                case 3: return new PointF(1, 0);     // Right
                case 4: return new PointF(-1, -1);   // UpLeft
                case 5: return new PointF(1, -1);    // UpRight
                case 6: return new PointF(-1, 1);    // DownLeft
                case 7: return new PointF(1, 1);     // DownRight
                default: return null;                // Any
            }
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
