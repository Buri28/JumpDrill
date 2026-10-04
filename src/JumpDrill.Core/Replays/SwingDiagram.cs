using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Output;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 振りの軌道を1枚の絵にする。
    /// </summary>
    /// <remarks>
    /// GUI の <c>ReplayView</c> と同じものを、画像ライブラリ無しで描く。
    /// あちらは WinForms の GDI で描いていてゲーム内では動かないので、
    /// <see cref="PixelCanvas"/> に落として PNG のバイト列で返す。
    /// MOD 側はそれを <c>Texture2D.LoadImage</c> に渡すだけで済む。
    ///
    /// 描くのは<b>ブレード先端</b>。リプレイに入っているのはコントローラの位置で、
    /// そこはノーツから 1 m ほど離れているので、そのまま描くとノーツと噛み合わない
    /// （<see cref="ReplayTransform.Tip"/>）。
    ///
    /// 振りを全部重ねるので、<b>再現性がそのまま束の太さとして見える。</b>
    /// 数値で出しているものを目で確かめるための絵。
    /// </remarks>
    public static class SwingDiagram
    {
        /// <summary>どの向きから見た図か。</summary>
        public enum ViewAxis
        {
            /// <summary>正面 (X-Y)。ノーツの格子がそのまま出る。</summary>
            Front,

            /// <summary>
            /// 横 (Z-Y)。<b>奥行きが見える。</b>
            /// 正面図では振りかぶりも振り抜きも画面の手前・奥に潰れて見えないので、
            /// PRE / POST が低いのに絵では真っ直ぐに見える、ということが起きる。
            /// </summary>
            Side,
        }

        public sealed class Options
        {
            public int Width { get; set; } = 512;
            public int Height { get; set; } = 384;

            public ViewAxis Axis { get; set; } = ViewAxis.Side;

            /// <summary>横から見た図で、奥（ノーツの来る方）を右にするか。</summary>
            public bool FaceRight { get; set; } = true;

            /// <summary>描く手。null なら両手。0 = 左、1 = 右。</summary>
            public int? OnlyHand { get; set; }

            /// <summary>平均軌道だけを描くか。1本1本の束を出さない。</summary>
            public bool MeanOnly { get; set; }

            /// <summary>10cm の目盛りを敷くか。</summary>
            public bool Grid { get; set; } = true;
        }

        // GUI の ReplayView と同じ色。並べたときに別物に見えないように合わせてある。
        private static readonly byte[] Background = { 0x14, 0x16, 0x1A };
        private static readonly byte[] GridColor = { 0x2A, 0x2E, 0x36 };
        private static readonly byte[] RightColor = { 0x2E, 0x86, 0xD8 };
        private static readonly byte[] LeftColor = { 0xD0, 0x3A, 0x3A };
        private static readonly byte[] CutMarkColor = { 0xF2, 0xF5, 0xFA };

        /// <summary>左手の平均軌道に置く「く」の色。赤の平均軌道は淡いので、少し明るい赤で乗せる（GUI と同じ）。</summary>
        private static readonly byte[] LeftMarkColor = { 0xE6, 0x55, 0x55 };

        /// <summary>描くノーツ1個。位置は本体の格子、向きはリプレイから出したもの。</summary>
        private sealed class Note
        {
            public int ColorType;

            /// <summary>本体の格子上の位置 (m)。</summary>
            public double CenterX, CenterY;

            /// <summary>
            /// 傾きを決めるための位置。<b>飛距離の半分の地点の高さ</b>。
            /// 本体の角度オフセットは到達時の高さではなく、こちらで向きを決めている。
            /// </summary>
            public double TiltX, TiltY;

            public int CutDirection;

            /// <summary>ノーツの傾き（画面上、時計回りの度）。向き指定なしなら null。</summary>
            public double? Tilt;

            /// <summary>切った瞬間の奥行き。プレイヤーの方を向く量がこれで決まる。</summary>
            public float Depth;

            /// <summary>面の右向き・上向きを X-Y へ落としたもの（プレイヤーを向いた後）。</summary>
            public double RightX, RightY, UpX, UpY;

            /// <summary>実際に振った向きの平均（X-Y）。振っていなければゼロ。</summary>
            public double SwungX, SwungY;

            /// <summary>この配置のノーツが出てくる時刻 (秒)。角度オフセットの相手探しに使う。</summary>
            public readonly List<float> Times = new List<float>();
        }

        private sealed class Swing
        {
            public int SaberType;
            public Vector3[] Path;

            /// <summary>その手の平均軌道からのずれ (m)。濃さに使う。</summary>
            public double Deviation;
        }

        /// <summary>
        /// PNG のバイト列を返す。描けるものが無ければ null。
        /// </summary>
        public static byte[] Render(Replay replay, Options options)
        {
            options = options ?? new Options();

            var canvas = Draw(replay, options);
            return canvas == null ? null : PngWriter.Encode(canvas.Pixels, options.Width, options.Height);
        }

        /// <summary>正面と横を左右に並べて1枚にする。GUI と同じ並び。</summary>
        /// <remarks>
        /// <see cref="Options.Width"/> は<b>2枚ぶんを合わせた幅</b>。
        /// 1枚ずつ別の画像にして UI で横に並べる形にもできるが、
        /// 縮尺と余白が別々に決まるので、同じ譜面なのに左右で大きさが変わって見える。
        /// </remarks>
        public static byte[] RenderBoth(Replay replay, Options options)
        {
            options = options ?? new Options();

            const int Divider = 4;
            int half = (options.Width - Divider) / 2;
            if (half <= 0) return null;

            var front = Draw(replay, Half(options, half, ViewAxis.Front));
            var side = Draw(replay, Half(options, half, ViewAxis.Side));
            if (front == null && side == null) return null;

            var canvas = new PixelCanvas(options.Width, options.Height);
            canvas.Fill(Background[0], Background[1], Background[2]);

            if (front != null) Blit(canvas, options.Width, front, half, options.Height, 0);
            if (side != null) Blit(canvas, options.Width, side, half, options.Height, half + Divider);

            // 仕切り。無いと軌道が左右でつながって見える
            for (int x = half; x < half + Divider; x++)
                canvas.LineAA(x, 0, x, options.Height, GridColor[0], GridColor[1], GridColor[2]);

            return PngWriter.Encode(canvas.Pixels, options.Width, options.Height);
        }

        private static Options Half(Options source, int width, ViewAxis axis)
        {
            return new Options
            {
                Width = width,
                Height = source.Height,
                Axis = axis,
                FaceRight = source.FaceRight,
                OnlyHand = source.OnlyHand,
                MeanOnly = source.MeanOnly,
                Grid = source.Grid,
            };
        }

        private static void Blit(PixelCanvas into, int intoWidth,
                                 PixelCanvas from, int fromWidth, int height, int atX)
        {
            for (int y = 0; y < height; y++)
            {
                int source = y * fromWidth * 3;
                int target = (y * intoWidth + atX) * 3;
                Array.Copy(from.Pixels, source, into.Pixels, target, fromWidth * 3);
            }
        }

        private static PixelCanvas Draw(Replay replay, Options options)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));

            var swings = new List<Swing>();
            var means = new List<Swing>();

            // 両手とも読んでから、描くときに絞る。範囲を両手ぶんで取らないと、
            // 片手だけにしたときに縮尺が変わって GUI の絵と大きさが合わない
            foreach (int saberType in new[] { 0, 1 })
                LoadHand(replay, saberType, swings, means);

            if (swings.Count == 0) return null;

            var notes = options.Axis == ViewAxis.Front ? LoadNotes(replay) : new List<Note>();
            var bounds = ComputeBounds(swings, notes, options);

            if (options.OnlyHand.HasValue)
            {
                int hand = options.OnlyHand.Value;
                swings = swings.Where(s => s.SaberType == hand).ToList();
                means = means.Where(s => s.SaberType == hand).ToList();
                notes = notes.Where(n => n.ColorType == hand).ToList();
            }
            var canvas = new PixelCanvas(options.Width, options.Height);
            canvas.Fill(Background[0], Background[1], Background[2]);

            if (options.Grid) DrawGrid(canvas, bounds, options);

            // ノーツの下地は束より先に。上に乗せると軌道が読めない
            foreach (var note in notes) DrawNoteBody(canvas, bounds, options, note);

            if (!options.MeanOnly)
            {
                foreach (var swing in swings)
                {
                    var color = swing.SaberType == 0 ? LeftColor : RightColor;

                    // 平均から外れた振りほど目立たせる。GUI と同じ出し方
                    double alpha = Math.Min(200.0, 40.0 + swing.Deviation * 1600.0) / 255.0;
                    DrawPath(canvas, bounds, options, swing.Path, color, 1.4, alpha);
                }
            }

            if (options.Axis == ViewAxis.Front)
            {
                // 枠と矢印と軸は最後に。束の下に隠れると位置も角度も読めない
                DrawAxis(canvas, bounds, options, notes);
                foreach (var note in notes) DrawNoteEdge(canvas, bounds, options, note);
                foreach (var note in notes) DrawSwungDirection(canvas, bounds, options, note);
            }
            else
            {
                foreach (var mean in means)
                {
                    if (mean.Path.Length == 0) continue;

                    var dot = ToScreen(Project(mean.Path[mean.Path.Length - 1], options), bounds, options);
                    canvas.Disc(dot[0], dot[1], 4.0, CutMarkColor[0], CutMarkColor[1], CutMarkColor[2]);
                }
            }

            // 平均軌道は最後に。束やノーツの枠の下に隠れると形が読めない（GUI と同じ順）
            foreach (var mean in means)
            {
                var color = mean.SaberType == 0 ? LeftColor : RightColor;
                DrawPath(canvas, bounds, options, mean.Path, Lighten(color, 0.4), 3.2, 1.0);
                DrawDirectionMarks(canvas, bounds, options, mean.Path, mean.SaberType == 0 ? LeftMarkColor : color);
            }

            return canvas;
        }

        /// <summary>
        /// 手1つぶんの軌道を集める。
        /// </summary>
        /// <remarks>
        /// 遷移ごとに束ねる。往路と復路を混ぜると別物が重なって、
        /// 束の太さが再現性を表さなくなる。
        /// </remarks>
        private static void LoadHand(Replay replay, int saberType, List<Swing> swings, List<Swing> means)
        {
            var cuts = replay.Notes
                .Where(n => n.EventType == NoteEventType.Good && n.Cut != null && n.Cut.SaberType == saberType)
                .OrderBy(n => n.EventTime)
                .ToList();

            if (cuts.Count < 2) return;

            var groups = new Dictionary<string, List<Vector3[]>>();

            // 振りは数字と同じ決め方。間でミスしたところは前後をつながない
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
                    swings.Add(new Swing
                    {
                        SaberType = saberType,
                        Path = path,
                        Deviation = MeanDistance(path, mean),
                    });

                means.Add(new Swing { SaberType = saberType, Path = mean });
            }
        }

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

        /// <summary>ノーツの一辺 (m)。本体の見た目に合わせた大きさ。</summary>
        private const double NoteSize = 0.5;

        /// <summary>描画範囲 (left, bottom, width, height)。全ての軌道とノーツが入るように取る。</summary>
        private static double[] ComputeBounds(List<Swing> swings, List<Note> notes, Options options)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var swing in swings)
                foreach (var p in swing.Path)
                {
                    var q = Project(p, options);
                    if (q[0] < minX) minX = q[0];
                    if (q[0] > maxX) maxX = q[0];
                    if (q[1] < minY) minY = q[1];
                    if (q[1] > maxY) maxY = q[1];
                }

            // ノーツが枠の外に出ると位置関係が読めない
            foreach (var note in notes)
            {
                minX = Math.Min(minX, note.CenterX - NoteSize / 2);
                maxX = Math.Max(maxX, note.CenterX + NoteSize / 2);
                minY = Math.Min(minY, note.CenterY - NoteSize / 2);
                maxY = Math.Max(maxY, note.CenterY + NoteSize / 2);
            }

            double padX = Math.Max(0.1, (maxX - minX) * 0.12);
            double padY = Math.Max(0.1, (maxY - minY) * 0.12);

            return new[]
            {
                minX - padX,
                minY - padY,
                (maxX - minX) + padX * 2,
                (maxY - minY) + padY * 2,
            };
        }

        /// <summary>
        /// 世界座標を、いま見ている平面へ落とす。縦は<b>どちらの図でも高さ</b>。
        /// 横から見るときは奥行きを左右に使う。
        /// </summary>
        private static double[] Project(Vector3 p, Options options)
        {
            if (options.Axis != ViewAxis.Side) return new double[] { p.X, p.Y };
            return new double[] { options.FaceRight ? p.Z : -p.Z, p.Y };
        }

        /// <summary>平面の座標を画像の画素へ。画像は下が正なので Y を反転する。</summary>
        private static double[] ToScreen(double[] p, double[] bounds, Options options)
        {
            double scale = Math.Min(options.Width / bounds[2], options.Height / bounds[3]);
            double offsetX = (options.Width - bounds[2] * scale) / 2.0;
            double offsetY = (options.Height - bounds[3] * scale) / 2.0;

            return new[]
            {
                offsetX + (p[0] - bounds[0]) * scale,
                options.Height - offsetY - (p[1] - bounds[1]) * scale,
            };
        }

        private static void DrawPath(PixelCanvas canvas, double[] bounds, Options options,
                                     Vector3[] path, byte[] color, double thickness, double alpha)
        {
            for (int i = 1; i < path.Length; i++)
            {
                var a = ToScreen(Project(path[i - 1], options), bounds, options);
                var b = ToScreen(Project(path[i], options), bounds, options);
                canvas.LineAA(a[0], a[1], b[0], b[1], color[0], color[1], color[2], thickness, alpha);
            }
        }

        /// <summary>進む向きに開いた「く」を3か所に置く。どちら回りかが読めないと図の意味が半減する。</summary>
        private static void DrawDirectionMarks(PixelCanvas canvas, double[] bounds, Options options,
                                               Vector3[] path, byte[] color)
        {
            var points = new double[path.Length][];
            for (int i = 0; i < path.Length; i++)
                points[i] = ToScreen(Project(path[i], options), bounds, options);

            foreach (double at in new[] { 0.25, 0.5, 0.75 })
            {
                int index = Math.Max(1, (int)(at * (points.Length - 1)));

                double dx = points[index][0] - points[index - 1][0];
                double dy = points[index][1] - points[index - 1][1];
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 0.5) continue;

                dx /= length;
                dy /= length;

                const double Arm = 7.0;
                double px = -dy, py = dx;
                double tipX = points[index][0], tipY = points[index][1];

                canvas.LineAA(tipX - dx * Arm + px * Arm * 0.75, tipY - dy * Arm + py * Arm * 0.75,
                              tipX, tipY, color[0], color[1], color[2], 2.4);
                canvas.LineAA(tipX - dx * Arm - px * Arm * 0.75, tipY - dy * Arm - py * Arm * 0.75,
                              tipX, tipY, color[0], color[1], color[2], 2.4);
            }
        }

        /// <summary>
        /// 目盛り代わりの 10cm 格子。ずれの大きさを目で測れるように。
        /// 説明の字は入れない（<see cref="PixelCanvas"/> に文字を描く手段が無い）。
        /// </summary>
        private static void DrawGrid(PixelCanvas canvas, double[] bounds, Options options)
        {
            double left = bounds[0], bottom = bounds[1];
            double right = left + bounds[2], top = bottom + bounds[3];

            for (double x = Math.Ceiling(left * 10) / 10.0; x < right; x += 0.1)
            {
                var a = ToScreen(new[] { x, bottom }, bounds, options);
                var b = ToScreen(new[] { x, top }, bounds, options);
                canvas.LineAA(a[0], a[1], b[0], b[1], GridColor[0], GridColor[1], GridColor[2]);
            }

            for (double y = Math.Ceiling(bottom * 10) / 10.0; y < top; y += 0.1)
            {
                var a = ToScreen(new[] { left, y }, bounds, options);
                var b = ToScreen(new[] { right, y }, bounds, options);
                canvas.LineAA(a[0], a[1], b[0], b[1], GridColor[0], GridColor[1], GridColor[2]);
            }
        }

        // ───────── 正面図：ノーツ ─────────

        /// <summary>
        /// ノーツの置き場所と向きを集める。
        /// </summary>
        /// <remarks>
        /// 縦位置は身長ぶんの下駄を切った位置から当てはめたもの（<c>NoteGrid.FitTo</c>）なので、
        /// 数 cm はずれる。位置関係を見るためのもので、寸法を測るものではない。
        /// </remarks>
        private static List<Note> LoadNotes(Replay replay)
        {
            var notes = new List<Note>();

            double residual;
            var grid = NoteGrid.FitTo(replay, out residual);
            var cuts = NoteGrid.CutCenters(replay);

            foreach (var placement in NoteGrid.PlacementsOf(replay))
            {
                Vector3 measured;
                bool hasCut = cuts.TryGetValue(placement, out measured);

                double swungX, swungY, angleError;
                MeasureSwing(replay, placement, out swungX, out swungY, out angleError);

                var note = new Note
                {
                    ColorType = placement.ColorType,
                    CenterX = grid.X(placement.LineIndex),
                    CenterY = grid.Y(placement.LineLayer),
                    TiltX = grid.X(placement.LineIndex),
                    TiltY = NoteGrid.MidJumpY(placement.LineLayer),
                    CutDirection = placement.CutDirection,
                    Depth = hasCut ? measured.Z : 0f,
                    SwungX = swungX,
                    SwungY = swungY,
                };

                foreach (var played in replay.Notes)
                    if (played.ColorType == placement.ColorType &&
                        played.LineIndex == placement.LineIndex &&
                        played.LineLayer == placement.LineLayer &&
                        played.CutDirection == placement.CutDirection)
                        note.Times.Add(played.EventTime);

                notes.Add(note);
            }

            ApplyAngleOffsets(notes);
            ApplyLookAtPlayer(replay, notes);
            return notes;
        }

        /// <summary>
        /// その配置を実際にどの向きに振ったか（平均）と、要求された向きからのずれ。
        /// </summary>
        /// <remarks>
        /// 向きは <c>saberDir</c> を長さ 1 に直してから平均する。速さで重みが付くと、
        /// 強く振った回だけの向きになってしまう。
        /// </remarks>
        private static void MeasureSwing(Replay replay, NotePlacement placement,
                                         out double dirX, out double dirY, out double angleError)
        {
            dirX = 0.0;
            dirY = 0.0;
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

            dirX = x / norm;
            dirY = y / norm;
            angleError = error / count;
        }

        /// <summary>
        /// 本体の角度オフセットを当てる。<b>ノーツは相手との線に合わせて傾く</b>。
        /// </summary>
        /// <remarks>
        /// ただし<b>同じ拍に並んだ対にしか当たらない</b>。ドリルは片手が1拍に1個ずつなので、
        /// ふつうはどのノーツにも当たらない＝矢印の向きそのままで立つ。
        /// 時刻を見ずに配置だけで対を作ると、往復の2点が常に対になってしまい、
        /// <c>s</c> を付けた譜面で水平に立つはずのノーツが移動の軸に寝てしまう。
        /// 本体はずれが大きすぎると当てない。その境目が 40 度。
        /// </remarks>
        private static void ApplyAngleOffsets(List<Note> notes)
        {
            const double Limit = 40.0;

            foreach (int hand in new[] { 0, 1 })
            {
                var hands = notes.Where(n => n.ColorType == hand).ToList();

                foreach (var note in hands)
                {
                    double arrowX, arrowY;
                    if (!ArrowDirection(note.CutDirection, out arrowX, out arrowY)) continue;

                    note.Tilt = ToDegrees(arrowX, arrowY);

                    // 相手は、同じ拍に出ていて、かつ矢印が向いている先と逆にいる方
                    Note partner = null;
                    double bestDot = double.MinValue;

                    foreach (var other in hands)
                    {
                        if (other == note || !SameRow(note, other)) continue;

                        double dot = Dot(arrowX, arrowY,
                                         other.TiltX - note.TiltX, -(other.TiltY - note.TiltY));
                        if (dot > bestDot) { bestDot = dot; partner = other; }
                    }

                    if (partner == null) continue;

                    double lineX = note.TiltX - partner.TiltX;
                    double lineY = -(note.TiltY - partner.TiltY);
                    if (Math.Abs(lineX) < 1e-6 && Math.Abs(lineY) < 1e-6) continue;

                    // 相手との線には向きが無い（本体の SignedAngleToLine と同じ）。
                    // 線の2つの向きのうち矢印に近い方へ合わせる。片方の向きだけで測ると、
                    // 矢印が相手の方を向いているノーツでは差が 180° 近くになり、傾かない
                    double aligned = ToDegrees(lineX, lineY);
                    double difference = Wrap(aligned - note.Tilt.Value);
                    if (Math.Abs(difference) > 90)
                    {
                        aligned = ToDegrees(-lineX, -lineY);
                        difference = Wrap(aligned - note.Tilt.Value);
                    }

                    if (Math.Abs(difference) <= Limit) note.Tilt = aligned;
                }
            }
        }

        /// <summary>角度を -180〜180 に収める。</summary>
        private static double Wrap(double degrees)
        {
            while (degrees > 180) degrees -= 360;
            while (degrees < -180) degrees += 360;
            return degrees;
        }

        /// <summary>同じ拍に並んでいるか。本体が1拍ぶんを切り出すのに合わせた幅。</summary>
        private static bool SameRow(Note a, Note b)
        {
            const float Window = 0.01f;

            foreach (var one in a.Times)
                foreach (var other in b.Times)
                    if (Math.Abs(one - other) <= Window) return true;

            return false;
        }

        private static double Dot(double ax, double ay, double bx, double by)
        {
            double length = Math.Sqrt(bx * bx + by * by);
            return length < 1e-9 ? 0 : (ax * bx + ay * by) / length;
        }

        /// <summary>局所の上向き (0,-1) をこの向きへ回す角度（度）。</summary>
        private static double ToDegrees(double x, double y)
        {
            return Math.Atan2(x, -y) * 180.0 / Math.PI;
        }

        /// <summary>矢印の向き。画面は下が正。向き指定なし（8）は false。</summary>
        private static bool ArrowDirection(int cutDirection, out double x, out double y)
        {
            switch (cutDirection)
            {
                case 0: x = 0; y = -1; return true;   // Up
                case 1: x = 0; y = 1; return true;    // Down
                case 2: x = -1; y = 0; return true;   // Left
                case 3: x = 1; y = 0; return true;    // Right
                case 4: x = -1; y = -1; return true;  // UpLeft
                case 5: x = 1; y = -1; return true;   // UpRight
                case 6: x = -1; y = 1; return true;   // DownLeft
                case 7: x = 1; y = 1; return true;    // DownRight
                default: x = 0; y = 0; return false;  // Any
            }
        }

        /// <summary>
        /// ノーツをプレイヤーの方へ向ける（本体の <c>NoteJump::_rotateTowardsPlayer</c>）。
        /// 面が正面を向いていないと、矢印の見かけの角度が変わる。
        /// </summary>
        private static void ApplyLookAtPlayer(Replay replay, List<Note> notes)
        {
            var head = HeadAtCuts(replay);

            foreach (var note in notes)
            {
                // 回る前の「上」＝矢印の向き。傾きから戻す
                double radians = (note.Tilt ?? 0.0) * Math.PI / 180.0;
                var baseUp = new Vector3
                {
                    X = (float)Math.Sin(radians),
                    Y = (float)Math.Cos(radians),
                    Z = 0f,
                };

                var center = new Vector3 { X = (float)note.CenterX, Y = (float)note.CenterY, Z = note.Depth };

                Vector3 right, up;
                NotePose.LookAtPlayer(center, head, baseUp, out right, out up);

                note.RightX = right.X;
                note.RightY = right.Y;
                note.UpX = up.X;
                note.UpY = up.Y;
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

        // ───────── 正面図：描画 ─────────

        /// <summary>
        /// ノーツの四角の4隅を画像の画素で返す。
        /// </summary>
        /// <remarks>
        /// 面の右向き・上向きをそのまま軸に使う。斜めを向いたぶん縮んで見えるのが正しい
        /// （本体も同じ見え方をする）。画像は下が正なので Y を反転する。
        /// </remarks>
        private static void NoteAxes(Note note, out double rx, out double ry, out double ux, out double uy)
        {
            // 向き指定なし（ドット）は回さずに正面のまま置く（GUI の DrawNote と同じ）
            double arrowX, arrowY;
            if (!ArrowDirection(note.CutDirection, out arrowX, out arrowY))
            {
                rx = 1; ry = 0; ux = 0; uy = 1;
                return;
            }

            rx = note.RightX;
            ry = -note.RightY;
            ux = -note.UpX;
            uy = note.UpY;

            if (rx != 0 || ry != 0) return;

            // 向きが取れていなければ傾きだけで回す
            double radians = (note.Tilt ?? 0.0) * Math.PI / 180.0;
            rx = Math.Cos(radians);
            ry = Math.Sin(radians);
            ux = -Math.Sin(radians);
            uy = Math.Cos(radians);
        }

        private static double NoteSizeInPixels(double[] bounds, Options options)
        {
            double scale = Math.Min(options.Width / bounds[2], options.Height / bounds[3]);
            return NoteSize * scale;
        }

        private static void NoteQuad(double[] bounds, Options options, Note note,
                                     out double[] xs, out double[] ys)
        {
            var center = ToScreen(new[] { note.CenterX, note.CenterY }, bounds, options);
            double half = NoteSizeInPixels(bounds, options) / 2.0;

            double rx, ry, ux, uy;
            NoteAxes(note, out rx, out ry, out ux, out uy);

            var corners = new[]
            {
                new[] { -1.0, -1.0 }, new[] { 1.0, -1.0 }, new[] { 1.0, 1.0 }, new[] { -1.0, 1.0 },
            };

            xs = new double[4];
            ys = new double[4];

            for (int i = 0; i < 4; i++)
            {
                double lx = corners[i][0] * half, ly = corners[i][1] * half;
                xs[i] = center[0] + rx * lx + ux * ly;
                ys[i] = center[1] + ry * lx + uy * ly;
            }
        }

        /// <summary>下地。薄く塗って、どこにノーツがあるかだけ見せる。</summary>
        private static void DrawNoteBody(PixelCanvas canvas, double[] bounds, Options options, Note note)
        {
            var color = note.ColorType == 0 ? LeftColor : RightColor;

            double[] xs, ys;
            NoteQuad(bounds, options, note, out xs, out ys);
            canvas.FillPolygon(xs, ys, color[0], color[1], color[2], 120.0 / 255.0);
        }

        /// <summary>
        /// 縁と矢印と中心の点。本体（と BeatLeader の成績画面）と同じ見た目にする。
        /// 三角は切る向きと反対側に置き、先が切る向きを指す。
        /// </summary>
        private static void DrawNoteEdge(PixelCanvas canvas, double[] bounds, Options options, Note note)
        {
            var color = Lighten(note.ColorType == 0 ? LeftColor : RightColor, 0.35);

            double[] xs, ys;
            NoteQuad(bounds, options, note, out xs, out ys);

            double size = NoteSizeInPixels(bounds, options);
            double thickness = Math.Max(1.6, size * 0.05);

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                canvas.LineAA(xs[i], ys[i], xs[next], ys[next],
                              color[0], color[1], color[2], thickness, 235.0 / 255.0);
            }

            var center = ToScreen(new[] { note.CenterX, note.CenterY }, bounds, options);

            double arrowX, arrowY;
            if (ArrowDirection(note.CutDirection, out arrowX, out arrowY))
            {
                double rx, ry, ux, uy;
                NoteAxes(note, out rx, out ry, out ux, out uy);

                // 正三角形ではなく、幅の広い浅い形
                var local = new[]
                {
                    new[] { 0.0, size * 0.12 },
                    new[] { -size * 0.34, size * 0.40 },
                    new[] { size * 0.34, size * 0.40 },
                };

                var tx = new double[3];
                var ty = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    tx[i] = center[0] + rx * local[i][0] + ux * local[i][1];
                    ty[i] = center[1] + ry * local[i][0] + uy * local[i][1];
                }

                canvas.FillPolygon(tx, ty, CutMarkColor[0], CutMarkColor[1], CutMarkColor[2], 150.0 / 255.0);
            }

            // 点はノーツの中心。当たり所を測る基準になるので位置をずらさない
            canvas.Disc(center[0], center[1], Math.Max(1.0, size * 0.065),
                        CutMarkColor[0], CutMarkColor[1], CutMarkColor[2], 235.0 / 255.0);
        }

        /// <summary>実際に振った向きを示す線の長さ (m)。</summary>
        private const double SwungMarkLength = 0.34;

        /// <summary>
        /// ノーツごとに、実際に振った向きを線で出す。
        /// ノーツの矢印（要求された向き）との開きが、そのまま角度ずれになる。
        /// </summary>
        private static void DrawSwungDirection(PixelCanvas canvas, double[] bounds, Options options, Note note)
        {
            if (note.SwungX == 0 && note.SwungY == 0) return;

            var from = ToScreen(new[] { note.CenterX, note.CenterY }, bounds, options);
            var to = ToScreen(new[]
            {
                note.CenterX + note.SwungX * SwungMarkLength,
                note.CenterY + note.SwungY * SwungMarkLength,
            }, bounds, options);

            canvas.DashedLine(from[0], from[1], to[0], to[1],
                              CutMarkColor[0], CutMarkColor[1], CutMarkColor[2], 2.0, 230.0 / 255.0, 3.0);

            // 先端に印。GUI は矢尻を付けているが、ここでは点で代える
            canvas.Disc(to[0], to[1], 2.5, CutMarkColor[0], CutMarkColor[1], CutMarkColor[2], 230.0 / 255.0);
        }

        /// <summary>
        /// ノーツどうしを破線で結ぶ。
        /// </summary>
        /// <remarks>
        /// ノーツ四角の回転は 8 方向しか無いので、配置がその 8 方向に乗っていないと
        /// 軸とノーツの向きが食い違う。それを目で確かめられるようにする線。
        /// 角度の数値は出せない（文字を描く手段が無い）。
        /// </remarks>
        private static void DrawAxis(PixelCanvas canvas, double[] bounds, Options options, List<Note> notes)
        {
            foreach (int hand in new[] { 0, 1 })
            {
                var ordered = notes.Where(n => n.ColorType == hand).OrderBy(n => n.CenterY).ToList();
                if (ordered.Count < 2) continue;

                var color = Lighten(hand == 0 ? LeftColor : RightColor, 0.6);

                for (int i = 1; i < ordered.Count; i++)
                {
                    var a = ToScreen(new[] { ordered[i - 1].CenterX, ordered[i - 1].CenterY }, bounds, options);
                    var b = ToScreen(new[] { ordered[i].CenterX, ordered[i].CenterY }, bounds, options);

                    canvas.DashedLine(a[0], a[1], b[0], b[1],
                                      color[0], color[1], color[2], 1.4, 190.0 / 255.0);
                }
            }
        }

        /// <summary>白へ寄せる。GUI の <c>ControlPaint.Light</c> と同じ効き方にしてある。</summary>
        private static byte[] Lighten(byte[] color, double amount)
        {
            return new[]
            {
                (byte)(color[0] + (255 - color[0]) * amount),
                (byte)(color[1] + (255 - color[1]) * amount),
                (byte)(color[2] + (255 - color[2]) * amount),
            };
        }
    }
}
