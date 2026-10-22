using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Output;
using Swing = JumpDrill.Replays.SwingScene.Swing;

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
    ///
    /// 中身は GUI の図と共通（振りは <see cref="SwingScene"/>、ノーツは <see cref="NoteFigures"/>、色は <see cref="SwingPalette"/>）。
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

            /// <summary>平均軌道だけを描くか。1本1本の束を出さない。切った線も平均の1本にする。</summary>
            public bool MeanOnly { get; set; }

            /// <summary>
            /// フォアだけ（true）かバックだけ（false）を出す。両方なら null。
            /// タワー・スタックでは行きと帰りが同じ所を通るので、片方ずつ見られるようにする。
            /// </summary>
            public bool? OnlyForehand { get; set; }

            /// <summary>
            /// ノーツを良い切りの当たり判定の大きさで描くか（<see cref="NoteBox.Hitbox"/>）。false なら見た目の大きさ。
            /// 当たり判定は見た目より横に広く、手前に長いので、刃が見た目のノーツに届いていなくても切れていることがある。
            /// </summary>
            public bool Hitbox { get; set; }

            /// <summary>譜面の NJS。分かればノーツの向きを本体と同じ計算で出す。null なら近似。</summary>
            public float? NoteJumpSpeed { get; set; }

            /// <summary>10cm の目盛りを敷くか。</summary>
            public bool Grid { get; set; } = true;
        }

        private static byte[] Background { get { return SwingPalette.Background; } }
        private static byte[] GridColor { get { return SwingPalette.Grid; } }
        private static byte[] White { get { return SwingPalette.White; } }

        private const double NoteSize = NoteFigures.NoteSize;

        private static bool Shown(Options options, bool forehand)
        {
            return !options.OnlyForehand.HasValue || options.OnlyForehand.Value == forehand;
        }

        /// <summary>
        /// PNG のバイト列を返す。描けるものが無ければ null。
        /// </summary>
        public static byte[] Render(Replay replay, Options options)
        {
            options = options ?? new Options();
            var content = Content.Of(replay, options.NoteJumpSpeed);
            var canvas = Draw(content, options);
            return canvas == null ? null : PngWriter.Encode(canvas.Pixels, options.Width, options.Height);
        }

        /// <summary>正面と横を左右に並べて1枚にする。GUI と同じ並び。</summary>
        /// <remarks>
        /// <see cref="Options.Width"/> は<b>2枚ぶんを合わせた幅</b>。
        /// 1枚ずつ別の画像にして UI で横に並べる形にもできるが、
        /// 縮尺と余白が別々に決まるので、同じ譜面なのに左右で大きさが変わって見える。
        /// 中身（振りとノーツ）は1回だけ作って、2枚で使う。
        /// </remarks>
        public static byte[] RenderBoth(Replay replay, Options options)
        {
            options = options ?? new Options();

            const int Divider = 4;
            int half = (options.Width - Divider) / 2;
            if (half <= 0) return null;

            var content = Content.Of(replay, options.NoteJumpSpeed);
            var front = Draw(content, Half(options, half, ViewAxis.Front));
            var side = Draw(content, Half(options, half, ViewAxis.Side));
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

        /// <summary>描く中身。軌道はタワーを端の点だけのジャンプとして（採点と同じ）、ノーツはまとめる前の全ノーツで置く。</summary>
        private sealed class Content
        {
            public SwingScene Scene;
            public List<NoteFigure> Notes;

            public static Content Of(Replay replay, float? noteJumpSpeed)
            {
                if (replay == null) throw new ArgumentNullException(nameof(replay));
                return new Content
                {
                    Scene = SwingScene.Build(TowerReplay.Collapse(replay), withNotes: false),
                    Notes = NoteFigures.Build(replay, noteJumpSpeed: noteJumpSpeed),
                };
            }
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
                OnlyForehand = source.OnlyForehand,
                Hitbox = source.Hitbox,
                NoteJumpSpeed = source.NoteJumpSpeed,
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

        private static PixelCanvas Draw(Content content, Options options)
        {
            var swings = content.Scene.Swings;
            var means = content.Scene.Means;
            var notes = content.Notes;
            if (swings.Count == 0) return null;

            // 範囲は両手ぶんで取る。片手だけにしたときに縮尺が変わって GUI の絵と大きさが合わないため
            var bounds = ComputeBounds(swings, notes, options);

            if (options.OnlyHand.HasValue)
            {
                int hand = options.OnlyHand.Value;
                swings = swings.Where(s => s.SaberType == hand).ToList();
                means = means.Where(s => s.SaberType == hand).ToList();
                notes = notes.Where(n => n.ColorType == hand).ToList();
            }

            // フォアだけ・バックだけのときは、その向きのノーツだけ（向き指定なしのノーツはどちらでも出す）
            notes = notes.Where(n => SwingSides.NoteShown(n.CutDirection, n.ColorType, options.OnlyForehand)).ToList();

            // 同じマスにまとめたノーツは、残ったうちの最初の1つが四角と中心の点を描く
            var bodies = new HashSet<NoteFigure>(notes.GroupBy(n => new { n.ColorType, n.LineIndex, n.LineLayer }).Select(g => g.First()));

            var canvas = new PixelCanvas(options.Width, options.Height);
            canvas.Fill(Background[0], Background[1], Background[2]);

            if (options.Grid) DrawGrid(canvas, bounds, options);

            // ノーツの下地は束より先に。上に乗せると軌道が読めない
            if (options.Axis == ViewAxis.Front)
                foreach (var note in notes)
                    if (bodies.Contains(note)) DrawNoteBody(canvas, bounds, options, note);

            if (!options.MeanOnly)
            {
                foreach (var swing in swings)
                {
                    // 平均から外れた振りほど目立たせる。GUI と同じ出し方
                    double alpha = Math.Min(200.0, 40.0 + swing.Deviation * 1600.0) / 255.0;
                    DrawSwing(canvas, bounds, options, swing, false, 1.6, alpha);
                }
            }

            if (options.Axis == ViewAxis.Front)
            {
                // 枠と矢印は最後に。束の下に隠れると位置も角度も読めない
                foreach (var note in notes) DrawNoteEdge(canvas, bounds, options, note, !bodies.Contains(note));
                foreach (var note in notes) DrawCutLines(canvas, bounds, options, note);
            }
            else
            {
                foreach (var note in notes) DrawSideNote(canvas, bounds, options, note);
            }

            // 平均軌道は最後に。束やノーツの枠の下に隠れると形が読めない（GUI と同じ順）
            foreach (var mean in means)
            {
                DrawSwing(canvas, bounds, options, mean, true, 3.2, 1.0);
                DrawDirectionMarks(canvas, bounds, options, mean);
            }

            // 先端がノーツの向こう（奥）を通ったノーツは、軌道の上に重ねて描き直す（ノーツの方が手前にある。GUI と同じ）
            if (options.Axis == ViewAxis.Front)
                foreach (var note in notes.Where(n => n.TipBehind))
                {
                    // 濃く塗って、軌道がノーツの後ろに隠れて見えるようにする
                    if (bodies.Contains(note)) DrawNoteBody(canvas, bounds, options, note, 215.0 / 255.0);
                    DrawNoteEdge(canvas, bounds, options, note, !bodies.Contains(note));
                    DrawCutLines(canvas, bounds, options, note);
                }

            // ヒットボックスに当たった所（黄色）と、そこからの平面の線（白い線）の上の点
            foreach (var note in notes) DrawBladeLink(canvas, bounds, options, note);

            return canvas;
        }

        /// <summary>描画範囲 (left, bottom, width, height)。全ての軌道とノーツが入るように取る。</summary>
        private static double[] ComputeBounds(List<Swing> swings, List<NoteFigure> notes, Options options)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var swing in swings)
                foreach (var p in swing.Tips)
                {
                    var q = Project(p, options);
                    if (q[0] < minX) minX = q[0];
                    if (q[0] > maxX) maxX = q[0];
                    if (q[1] < minY) minY = q[1];
                    if (q[1] > maxY) maxY = q[1];
                }

            // ノーツが枠の外に出ると位置関係が読めない。横の図は切った瞬間のノーツ（先端より奥にあることが多い）
            foreach (var note in notes)
            {
                double cx, cy;
                if (options.Axis == ViewAxis.Side)
                {
                    if (!note.HasCutCenter) continue;
                    var side = Project(note.CutCenter, options);
                    cx = side[0];
                    cy = side[1];
                }
                else
                {
                    cx = note.CenterX;
                    cy = note.CenterY;
                }

                minX = Math.Min(minX, cx - NoteSize / 2);
                maxX = Math.Max(maxX, cx + NoteSize / 2);
                minY = Math.Min(minY, cy - NoteSize / 2);
                maxY = Math.Max(maxY, cy + NoteSize / 2);

                // ヒットボックスを出していなくても範囲に入れる。切り替えで拡大率が変わると見比べられない
                foreach (var p in HitboxOutline(note, options))
                {
                    minX = Math.Min(minX, p[0]);
                    maxX = Math.Max(maxX, p[0]);
                    minY = Math.Min(minY, p[1]);
                    maxY = Math.Max(maxY, p[1]);
                }
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

        /// <summary>
        /// 振りを区間ごとに、フォア・バックの色で描く。フォアだけ・バックだけのときは出さない区間を飛ばす。
        /// </summary>
        /// <param name="mean">平均軌道か（明るくして太く描く）。</param>
        private static void DrawSwing(PixelCanvas canvas, double[] bounds, Options options,
                                      Swing swing, bool mean, double thickness, double alpha)
        {
            for (int i = 1; i < swing.Tips.Length; i++)
            {
                bool fore = swing.SegmentForehand(i);
                if (!Shown(options, fore)) continue;

                var color = SwingPalette.Of(swing.SaberType, fore);
                if (mean) color = Lighten(color, 0.4);

                var a = ToScreen(Project(swing.Tips[i - 1], options), bounds, options);
                var b = ToScreen(Project(swing.Tips[i], options), bounds, options);
                canvas.LineAA(a[0], a[1], b[0], b[1], color[0], color[1], color[2], thickness, alpha);
            }
        }

        /// <summary>進む向きに開いた「く」を3か所に置く。どちら回りかが読めないと図の意味が半減する。</summary>
        private static void DrawDirectionMarks(PixelCanvas canvas, double[] bounds, Options options, Swing swing)
        {
            var path = swing.Tips;
            var points = new double[path.Length][];
            for (int i = 0; i < path.Length; i++)
                points[i] = ToScreen(Project(path[i], options), bounds, options);

            foreach (double at in new[] { 0.25, 0.5, 0.75 })
            {
                int index = Math.Max(1, (int)(at * (points.Length - 1)));
                bool fore = swing.SegmentForehand(index);
                if (!Shown(options, fore)) continue;
                var color = SwingPalette.MarkOf(swing.SaberType, fore);

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

        /// <summary>
        /// ノーツのどこを切ったか。1回ごとの切った線を薄く重ねる。平均だけのときは平均の1本を太く、先に矢印を付ける。
        /// 中心の点から線までの距離が、点数の中心の 15 点に効くずれ（cutDistanceToCenter）。
        /// </summary>
        private static void DrawCutLines(PixelCanvas canvas, double[] bounds, Options options, NoteFigure note)
        {
            if (note.Cuts.Count == 0) return;

            var lines = options.MeanOnly ? new List<double[][]> { NoteFigures.MeanCut(note.Cuts) } : note.Cuts;
            double thickness = options.MeanOnly ? 2.4 : 1.2;
            double alpha = options.MeanOnly ? 240.0 / 255.0 : 80.0 / 255.0;

            foreach (var line in lines)
            {
                // 面の座標 (u, v) → 正面の平面。面の右・上を X-Y へ落としたもので、四角と同じだけ縮む
                var screen = line.Select(p => ToScreen(new[]
                {
                    note.CenterX + note.RightX * p[0] + note.UpX * p[1],
                    note.CenterY + note.RightY * p[0] + note.UpY * p[1],
                }, bounds, options)).ToArray();

                for (int i = 1; i < screen.Length; i++)
                    canvas.LineAA(screen[i - 1][0], screen[i - 1][1], screen[i][0], screen[i][1],
                                  White[0], White[1], White[2], thickness, alpha);

                if (!options.MeanOnly || screen.Length < 2) continue;

                // 線の先が振った向き（振り抜いた側）
                var tip = screen[screen.Length - 1];
                var back = screen[screen.Length - 2];
                double dx = tip[0] - back[0], dy = tip[1] - back[1];
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 0.1) continue;
                dx /= length; dy /= length;
                const double Arrow = 7.0;
                canvas.FillPolygon(
                    new[] { tip[0] + dx * 2, tip[0] - dx * Arrow - dy * Arrow * 0.55, tip[0] - dx * Arrow + dy * Arrow * 0.55 },
                    new[] { tip[1] + dy * 2, tip[1] - dy * Arrow + dx * Arrow * 0.55, tip[1] - dy * Arrow - dx * Arrow * 0.55 },
                    White[0], White[1], White[2], alpha);
            }
        }

        /// <summary>
        /// 横から見た図のノーツ。切った瞬間の中心（平均）に、向きを付けた立方体（ヒットボックスなら箱）を横から見た形で置く。
        /// 見える面ごとに明るさを変えて塗り、見える辺を引き、手前の面に ▽、左右の面に ⇩ の印を貼る（GUI と同じ）。色はフォア・バック。
        /// 先端の印（白丸）が箱の手前にあれば、刃の先でノーツの手前側をかすめて切っている。
        /// </summary>
        private static void DrawSideNote(PixelCanvas canvas, double[] bounds, Options options, NoteFigure note)
        {
            if (!note.HasCutCenter) return;

            var color = SwingPalette.Of(note.ColorType, note.Forehand);
            var c = note.CutCenter;

            var box = options.Hitbox ? NoteBox.Hitbox : NoteBox.Visual;

            // 箱を横から見た形（3D と同じ見え方）。見える面を塗り、見える辺を引く
            var cube = box.Corners(note.CutCenter, note.FaceRight, note.FaceUp, note.FaceForward)
                          .Select(p => ToScreen(Project(p, options), bounds, options)).ToArray();

            var edge = Lighten(color, 0.35);
            var toViewer = new Vector3 { X = options.FaceRight ? 1f : -1f };   // 奥が右なら、プレイヤーの右側（+X）から見ている（Unity は左手系）

            // 見える面ごとに明るさを変えて塗る（輪郭だけだと立方体が裏返って見えることがある）
            foreach (var face in NoteBox.VisibleFaces(note.FaceRight, note.FaceUp, note.FaceForward, toViewer))
            {
                double shade = NoteBox.FaceShade(face.Value, toViewer);
                var lit = Lighten(color, shade * 0.6);
                canvas.FillPolygon(face.Key.Select(i => cube[i][0]).ToArray(), face.Key.Select(i => cube[i][1]).ToArray(),
                                   lit[0], lit[1], lit[2], (30 + 70 * shade) / 255.0);
            }
            foreach (var e in NoteBox.VisibleEdges(note.FaceRight, note.FaceUp, note.FaceForward, toViewer))
                canvas.LineAA(cube[e[0]][0], cube[e[0]][1], cube[e[1]][0], cube[e[1]][1], edge[0], edge[1], edge[2], 1.6, 220.0 / 255.0);

            // 切る向きの三角は立方体の面に貼る（3D と同じ）。手前の面と左右の面のうち見えるものに描くので、
            // 面と一緒に傾き、どの面のどちら向きかで読める
            double arrowX, arrowY;
            if (!SwingSides.ArrowOf(note.CutDirection, out arrowX, out arrowY)) return;

            foreach (var tri in box.ArrowDecals(c, note.FaceRight, note.FaceUp, note.FaceForward, toViewer))
            {
                var points = tri.Select(p => ToScreen(Project(p, options), bounds, options)).ToArray();
                canvas.FillPolygon(points.Select(p => p[0]).ToArray(), points.Select(p => p[1]).ToArray(),
                                   White[0], White[1], White[2], 200.0 / 255.0);
            }
        }

        // ───────── 正面図：ノーツ ─────────

        /// <summary>
        /// ノーツの面の右向き・上向き（画像の画素の向き）。
        /// </summary>
        /// <remarks>
        /// 面の右向き・上向きをそのまま軸に使う。斜めを向いたぶん縮んで見えるのが正しい
        /// （本体も同じ見え方をする）。画像は下が正なので Y を反転する。
        /// </remarks>
        private static void NoteAxes(NoteFigure note, out double rx, out double ry, out double ux, out double uy)
        {
            // 向き指定なし（ドット）は回さずに正面のまま置く（GUI の DrawNote と同じ）
            if (!note.Tilt.HasValue)
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
            double radians = note.Tilt.Value * Math.PI / 180.0;
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

        /// <summary>
        /// 当たり判定の箱の輪郭（図の座標、m）。正面の図はノーツの絵の位置に置いた箱を正面から見た輪郭、横の図は箱を横から見た輪郭。
        /// 正面の図はノーツの中心（<see cref="NoteFigure.CenterX"/>）、横の図は切った瞬間の中心に置く。
        /// </summary>
        private static List<double[]> HitboxOutline(NoteFigure note, Options options)
        {
            return Outline(note, options, NoteBox.Hitbox);
        }

        private static List<double[]> Outline(NoteFigure note, Options options, NoteBox box)
        {
            if (options.Axis == ViewAxis.Side)
            {
                if (!note.HasCutCenter) return new List<double[]>();
                return NoteBox.Hull(box.Corners(note.CutCenter, note.FaceRight, note.FaceUp, note.FaceForward)
                                       .Select(p => Project(p, options)));
            }

            return NoteBox.Hull(FrontCorners(note, box).Select(p => new double[] { p.X, p.Y }));
        }

        /// <summary>正面の図に置く箱の 8 隅（ノーツの絵の位置。向き指定なしは回さない。GUI と同じ）。</summary>
        private static Vector3[] FrontCorners(NoteFigure note, NoteBox box)
        {
            var center = new Vector3 { X = (float)note.CenterX, Y = (float)note.CenterY, Z = note.Depth };
            return note.Tilt.HasValue
                ? box.Corners(center, note.FaceRight, note.FaceUp, note.FaceForward)
                : box.Corners(center, new Vector3 { X = 1f }, new Vector3 { Y = 1f }, new Vector3 { Z = 1f });
        }

        private static void FrontAxes(NoteFigure note, out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            right = note.Tilt.HasValue ? note.FaceRight : new Vector3 { X = 1f };
            up = note.Tilt.HasValue ? note.FaceUp : new Vector3 { Y = 1f };
            forward = note.Tilt.HasValue ? note.FaceForward : new Vector3 { Z = 1f };
        }

        private static void NoteQuad(double[] bounds, Options options, NoteFigure note,
                                     out double[] xs, out double[] ys)
        {
            if (options.Hitbox)
            {
                var outline = HitboxOutline(note, options).Select(p => ToScreen(p, bounds, options)).ToArray();
                xs = outline.Select(p => p[0]).ToArray();
                ys = outline.Select(p => p[1]).ToArray();
                return;
            }

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
        /// <param name="alpha">塗りの濃さ (0-1)。</param>
        private static void DrawNoteBody(PixelCanvas canvas, double[] bounds, Options options, NoteFigure note, double alpha = 120.0 / 255.0)
        {
            var color = note.ColorType == 0 ? SwingPalette.Left : SwingPalette.Right;

            if (options.Hitbox)
            {
                // ヒットボックスは箱を正面から見た形（横の図と同じ）。見える面を明暗で塗る
                var corners = FrontCorners(note, NoteBox.Hitbox).Select(p => ToScreen(new double[] { p.X, p.Y }, bounds, options)).ToArray();
                Vector3 right, up, forward;
                FrontAxes(note, out right, out up, out forward);
                var toViewer = new Vector3 { Z = -1f };
                foreach (var face in NoteBox.VisibleFaces(right, up, forward, toViewer))
                {
                    double shade = NoteBox.FaceShade(face.Value, toViewer);
                    var lit = Lighten(color, shade * 0.5);
                    canvas.FillPolygon(face.Key.Select(i => corners[i][0]).ToArray(), face.Key.Select(i => corners[i][1]).ToArray(),
                                       lit[0], lit[1], lit[2], alpha * (0.4 + 0.6 * shade));
                }
                return;
            }

            double[] xs, ys;
            NoteQuad(bounds, options, note, out xs, out ys);
            canvas.FillPolygon(xs, ys, color[0], color[1], color[2], alpha);
        }

        /// <summary>
        /// 縁と矢印と中心の点。本体（と BeatLeader の成績画面）と同じ見た目にする。
        /// 三角は切る向きと反対側に置き、先が切る向きを指す。
        /// </summary>
        /// <param name="headOnly">三角だけ描く（同じマスにまとめたノーツの2つ目以降）。</param>
        private static void DrawNoteEdge(PixelCanvas canvas, double[] bounds, Options options, NoteFigure note, bool headOnly)
        {
            var color = Lighten(note.ColorType == 0 ? SwingPalette.Left : SwingPalette.Right, 0.35);

            double[] xs, ys;
            NoteQuad(bounds, options, note, out xs, out ys);

            double size = NoteSizeInPixels(bounds, options);
            double thickness = Math.Max(1.6, size * 0.05);

            if (!headOnly && options.Hitbox)
            {
                // ヒットボックスは見える辺を引く
                var corners = FrontCorners(note, NoteBox.Hitbox).Select(p => ToScreen(new double[] { p.X, p.Y }, bounds, options)).ToArray();
                Vector3 right, up, forward;
                FrontAxes(note, out right, out up, out forward);
                foreach (var e in NoteBox.VisibleEdges(right, up, forward, new Vector3 { Z = -1f }))
                    canvas.LineAA(corners[e[0]][0], corners[e[0]][1], corners[e[1]][0], corners[e[1]][1],
                                  color[0], color[1], color[2], 1.6, 235.0 / 255.0);
            }
            else if (!headOnly)
                for (int i = 0; i < xs.Length; i++)
                {
                    int next = (i + 1) % xs.Length;
                    canvas.LineAA(xs[i], ys[i], xs[next], ys[next],
                                  color[0], color[1], color[2], thickness, 235.0 / 255.0);
                }

            var center = ToScreen(new[] { note.CenterX, note.CenterY }, bounds, options);

            if (options.Hitbox)
            {
                // ヒットボックスは切る向きの印を箱の面に貼る（横の図と同じ。手前の面に ▽、見えていれば横の面に ⇩）
                double ax, ay;
                if (SwingSides.ArrowOf(note.CutDirection, out ax, out ay))
                {
                    Vector3 right, up, forward;
                    FrontAxes(note, out right, out up, out forward);
                    var center3 = new Vector3 { X = (float)note.CenterX, Y = (float)note.CenterY, Z = note.Depth };
                    foreach (var decal in NoteBox.Hitbox.ArrowDecals(center3, right, up, forward, new Vector3 { Z = -1f }))
                    {
                        var pts = decal.Select(p => ToScreen(new double[] { p.X, p.Y }, bounds, options)).ToArray();
                        canvas.FillPolygon(pts.Select(p => p[0]).ToArray(), pts.Select(p => p[1]).ToArray(),
                                           White[0], White[1], White[2], 170.0 / 255.0);
                    }
                }
            }
            else if (note.Tilt.HasValue)
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

                canvas.FillPolygon(tx, ty, White[0], White[1], White[2], 150.0 / 255.0);
            }

            // 点はノーツの中心。当たり所を測る基準になるので位置をずらさない
            if (!headOnly)
                canvas.Disc(center[0], center[1], Math.Max(1.0, size * 0.065),
                            White[0], White[1], White[2], 235.0 / 255.0);
        }

        /// <summary>
        /// ヒットボックスに当たったフレームの刃（白い線）、当たった所（黄色）から打点（黄色、白い切断線の上で中心に一番近い点）への矢印、
        /// 打点から中心への破線（GUI の DrawBladeLinks と同じ）。
        /// 正面の図はノーツの絵の上に、横の図は切った瞬間のノーツの中心から置く。
        /// </summary>
        private static void DrawBladeLink(PixelCanvas canvas, double[] bounds, Options options, NoteFigure note)
        {
            double[] contact, hit = null, bladeFrom = null, bladeTo = null;
            if (options.Axis == ViewAxis.Front)
            {
                double u, v;
                if (!note.TryContact(out u, out v)) return;
                contact = ToScreen(new[]
                {
                    note.CenterX + note.RightX * u + note.UpX * v,
                    note.CenterY + note.RightY * u + note.UpY * v,
                }, bounds, options);
                if (note.HasHitPoint && note.HasCutCenter)
                {
                    Func<Vector3, double[]> place = p => ToScreen(new[]
                    {
                        note.CenterX + p.X - note.CutCenter.X,
                        note.CenterY + p.Y - note.CutCenter.Y,
                    }, bounds, options);
                    hit = place(note.HitPoint);
                    bladeFrom = place(note.HitHilt);
                    bladeTo = place(note.HitTip);
                }
            }
            else
            {
                Vector3 at;
                if (!note.TryContactAtCut(out at)) return;
                contact = ToScreen(Project(at, options), bounds, options);
                if (note.HasHitPoint)
                {
                    hit = ToScreen(Project(note.HitPoint, options), bounds, options);
                    bladeFrom = ToScreen(Project(note.HitHilt, options), bounds, options);
                    bladeTo = ToScreen(Project(note.HitTip, options), bounds, options);
                }

                // 横の図にはノーツの中心の点が無いので、ここで打つ（正面の図はノーツの絵に描いてある）
                var middle = ToScreen(Project(note.CutCenter, options), bounds, options);
                canvas.Disc(middle[0], middle[1], 4.5, White[0], White[1], White[2], 245.0 / 255.0);
            }

            // ヒットボックスに当たったフレームの刃（3D と同じ白い線）。黄色い点（当たった所）はこの上にある
            if (bladeFrom != null && bladeTo != null)
                canvas.LineAA(bladeFrom[0], bladeFrom[1], bladeTo[0], bladeTo[1], White[0], White[1], White[2], 2.0, 230.0 / 255.0);

            // 打点から中心への破線（3D と同じ）。長さが中心からのずれ（中心の 15 点に効く）
            var center = options.Axis == ViewAxis.Front
                ? ToScreen(new[] { note.CenterX, note.CenterY }, bounds, options)
                : ToScreen(Project(note.CutCenter, options), bounds, options);
            canvas.DashedLine(contact[0], contact[1], center[0], center[1], White[0], White[1], White[2], 1.4, 220.0 / 255.0, 4.0);

            if (hit != null)
            {
                // 打点の側に矢印。矢尻が点に隠れないよう、点の縁で止める
                double dx = contact[0] - hit[0], dy = contact[1] - hit[1];
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 10.0)
                {
                    dx /= length;
                    dy /= length;
                    double endX = contact[0] - dx * 4.5, endY = contact[1] - dy * 4.5;
                    canvas.DashedLine(hit[0], hit[1], endX, endY, 0xFF, 0xD0, 0x60, 1.7, 230.0 / 255.0, 2.0);
                    const double Head = 5.0;
                    canvas.FillPolygon(
                        new[] { endX, endX - dx * Head + dy * Head * 0.55, endX - dx * Head - dy * Head * 0.55 },
                        new[] { endY, endY - dy * Head - dx * Head * 0.55, endY - dy * Head + dx * Head * 0.55 },
                        0xFF, 0xD0, 0x60, 230.0 / 255.0);
                }
                canvas.Disc(hit[0], hit[1], 3.5, 0xFF, 0xD0, 0x60, 235.0 / 255.0);
            }
            // 打点（平面の上で中心に一番近い点）は当たった所と同じ黄色。ノーツの中心（白）と見分ける
            canvas.Disc(contact[0], contact[1], 3.5, 0xFF, 0xD0, 0x60, 245.0 / 255.0);
        }

        /// <summary>明るくする。GUI の <c>ControlPaint.Light</c> と同じ計算（<see cref="SwingPalette.Light"/>）。</summary>
        private static byte[] Lighten(byte[] color, double amount)
        {
            return SwingPalette.Light(color, amount);
        }
    }
}
