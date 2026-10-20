using System;
using System.Collections.Generic;
using System.Linq;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 図に置くノーツ1個（配置＝手・マス・向きごと）。GUI の正面・横の図と MOD の図（<see cref="SwingDiagram"/>）で同じものを使う。
    /// </summary>
    public sealed class NoteFigure
    {
        public int ColorType { get; internal set; }
        public int LineIndex { get; internal set; }
        public int LineLayer { get; internal set; }
        public int CutDirection { get; internal set; }

        /// <summary>置く位置（X-Y、m）。本体の格子か、打点に寄せたもの。同じマスにまとめたノーツはそろえてある。</summary>
        public double CenterX { get; internal set; }
        public double CenterY { get; internal set; }

        /// <summary>本体の格子上の位置。打点に寄せても変わらない。</summary>
        public double GridX { get; internal set; }
        public double GridY { get; internal set; }

        /// <summary>
        /// 傾きを決めるための位置。<b>飛距離の半分の地点の高さ</b>（縦も 0.6 等間隔）。
        /// 本体の角度オフセットは <c>Get2DNoteOffset</c> の差で向きを決めていて、そこが使うのは
        /// <c>LineYPosForLineLayer</c>（0.25/0.85/1.45）。到達時の高さではないので、傾きだけ別の位置で測る。
        /// </summary>
        public double TiltX { get; internal set; }
        public double TiltY { get; internal set; }

        /// <summary>ノーツの傾き（画面上、時計回りの度）。向き指定なしなら null。</summary>
        public double? Tilt { get; internal set; }

        /// <summary>切った瞬間の奥行き。プレイヤーの方を向く量がこれで決まる。</summary>
        public float Depth { get; internal set; }

        /// <summary>実際に振った向きの平均（X-Y、長さ 1）。振っていなければゼロ。</summary>
        public double SwungX { get; internal set; }
        public double SwungY { get; internal set; }

        /// <summary>要求された向きからの角度ずれの平均 (度)。</summary>
        public double AngleError { get; internal set; }

        /// <summary>面の右向き・上向きを X-Y へ落としたもの（プレイヤーを向いた後）。</summary>
        public double RightX { get; internal set; }
        public double RightY { get; internal set; }
        public double UpX { get; internal set; }
        public double UpY { get; internal set; }

        /// <summary>
        /// 同じ手・同じマスに先に置いたノーツがある（タワー・スタックの行きと帰りなど）。
        /// 四角と中心の点はそちらで描くので、こちらは三角だけ描く。
        /// </summary>
        public bool Shared { get; internal set; }

        /// <summary>切った線（1回ごと）。点はノーツの面の座標（面の右, 面の上、m）で、真ん中の点が切った位置。</summary>
        public List<double[][]> Cuts { get; } = new List<double[][]>();

        /// <summary>
        /// 切った瞬間のノーツの中心（平均、世界座標）。横の図や 3D に置く。切っていなければ <see cref="HasCutCenter"/> が false。
        /// ノーツは飛んでくるので、切るのが早いほど奥、遅いほど手前になる。
        /// </summary>
        public Vector3 CutCenter { get; internal set; }
        public bool HasCutCenter { get; internal set; }

        /// <summary>フォアか（<see cref="SwingSides.NoteForehand"/>）。</summary>
        public bool Forehand { get { return SwingSides.NoteForehand(CutDirection); } }

        // 作る途中だけ使う
        internal readonly List<float> Times = new List<float>();
        internal readonly List<NoteCutInfo> RawCuts = new List<NoteCutInfo>();
        internal readonly List<Vector3[]> RawShapes = new List<Vector3[]>();
    }

    /// <summary>
    /// 図に置くノーツを作る（<see cref="NoteFigure"/>）。タワー・スタックをまとめる前のリプレイから置く。
    /// </summary>
    /// <remarks>
    /// 縦位置は身長ぶんの下駄を切った位置から当てはめたもの（<see cref="NoteGrid.FitTo"/>）なので、
    /// 数 cm はずれる。位置関係を見るためのもので、寸法を測るものではない。
    /// </remarks>
    public static class NoteFigures
    {
        /// <summary>ノーツの一辺 (m)。本体の見た目に合わせた大きさ。</summary>
        public const float NoteSize = 0.5f;

        /// <summary>切った線の点の数。奇数で、真ん中が切った瞬間。</summary>
        private const int CutLineSamples = 11;

        /// <param name="anchorToCuts">本体の格子ではなく、実際に切った位置（打点の平均）に置くか。</param>
        public static List<NoteFigure> Build(Replay replay, bool anchorToCuts = false)
        {
            var notes = new List<NoteFigure>();
            if (replay == null) return notes;

            double residual;
            var grid = NoteGrid.FitTo(replay, out residual);
            var cuts = NoteGrid.CutCenters(replay);

            foreach (var placement in NoteGrid.PlacementsOf(replay))
            {
                Vector3 measured;
                bool hasCut = cuts.TryGetValue(placement, out measured);

                double gridX = grid.X(placement.LineIndex), gridY = grid.Y(placement.LineLayer);
                var note = new NoteFigure
                {
                    ColorType = placement.ColorType,
                    LineIndex = placement.LineIndex,
                    LineLayer = placement.LineLayer,
                    CutDirection = placement.CutDirection,
                    GridX = gridX,
                    GridY = gridY,
                    CenterX = anchorToCuts && hasCut ? measured.X : gridX,
                    CenterY = anchorToCuts && hasCut ? measured.Y : gridY,
                    TiltX = gridX,
                    TiltY = NoteGrid.MidJumpY(placement.LineLayer),
                    Depth = hasCut ? measured.Z : 0f,
                };
                MeasureSwing(replay, placement, note);

                foreach (var played in replay.Notes)
                {
                    if (played.ColorType != placement.ColorType || played.LineIndex != placement.LineIndex ||
                        played.LineLayer != placement.LineLayer || played.CutDirection != placement.CutDirection) continue;

                    note.Times.Add(played.EventTime);
                    if (played.EventType == NoteEventType.Good && played.Cut != null && !played.IsStandIn)
                    {
                        note.RawCuts.Add(played.Cut);
                        note.RawShapes.Add(TipShape(replay, played));
                    }
                }

                notes.Add(note);
            }

            ApplyAngleOffsets(notes);
            MergeOverlaps(notes);
            ApplyLookAtPlayer(replay, notes);
            return notes;
        }

        /// <summary>切った線の平均。同じ番目の点どうしの平均で、形も位置も平均になる。</summary>
        public static double[][] MeanCut(List<double[][]> cuts)
        {
            int count = cuts[0].Length;
            var points = new double[count][];
            for (int k = 0; k < count; k++)
                points[k] = new[] { cuts.Average(c => c[k][0]), cuts.Average(c => c[k][1]) };
            return points;
        }

        /// <summary>打点の瞬間の頭の位置（平均）。</summary>
        public static Vector3 HeadAtCuts(Replay replay)
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
        /// 同じ手・同じマスのノーツを1つの四角にまとめる。タワー・スタックでは行きと帰りで
        /// 同じマスに向きの違うノーツが来るので、そのまま描くと四角が重なって読めない。
        /// 四角は1つだけ描き、三角はそれぞれの向きで描く（上下のタワーなら上と下の両方に付く）。
        /// 打点に寄せているときは、置き場所をそれらの打点の平均にする。
        /// </summary>
        private static void MergeOverlaps(List<NoteFigure> notes)
        {
            foreach (var group in notes.GroupBy(n => new { n.ColorType, n.LineIndex, n.LineLayer }))
            {
                var members = group.ToList();
                if (members.Count < 2) continue;

                double x = members.Average(n => n.CenterX), y = members.Average(n => n.CenterY);
                float depth = members.Average(n => n.Depth);
                for (int i = 0; i < members.Count; i++)
                {
                    members[i].CenterX = x;
                    members[i].CenterY = y;
                    members[i].Depth = depth;
                    members[i].Shared = i > 0;
                }
            }
        }

        /// <summary>
        /// その配置を実際にどの向きに振ったか（平均）と、要求された向きからのずれ。
        /// </summary>
        /// <remarks>
        /// 向きは <c>saberDir</c> を長さ 1 に直してから平均する。速さで重みが付くと、強く振った回だけの向きになってしまう。
        /// ずれの角度は本体が出している <c>cutDirDeviation</c> をそのまま使う（こちらで測り直すと角度オフセットを含められない）。
        /// タワー・スタックでは同じマスに向きの違うノーツが来るので、向きも見る（混ぜると上下の振りが打ち消し合う）。
        /// </remarks>
        private static void MeasureSwing(Replay replay, NotePlacement placement, NoteFigure figure)
        {
            double x = 0, y = 0, error = 0;
            int count = 0;

            foreach (var note in replay.Notes)
            {
                if (note.EventType != NoteEventType.Good || note.Cut == null) continue;
                if (note.ColorType != placement.ColorType) continue;
                if (note.LineIndex != placement.LineIndex || note.LineLayer != placement.LineLayer) continue;
                if (note.CutDirection != placement.CutDirection) continue;

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

            figure.SwungX = x / norm;
            figure.SwungY = y / norm;
            figure.AngleError = error / count;
        }

        /// <summary>
        /// 本体の角度オフセットを当てる。<b>ノーツは相手との線に合わせて傾く</b>。
        /// </summary>
        /// <remarks>
        /// <c>BeatmapObjectsInTimeRowProcessor</c> は同じ色のノーツ対を、<c>Get2DNoteOffset</c> の差（＝結ぶ線）と
        /// <c>Direction(cutDirection)</c> の <c>SignedAngleToLine</c> を取り、それを <c>SetCutDirectionAngleOffset</c> で足す。
        /// ただし<b>同じ拍に同じ色がちょうど2個のときだけ</b>（3連のタワーには当てない）。
        /// ドリルは片手が1拍に1個ずつなので、ふつうはどのノーツにも当たらない＝矢印の向きそのままで立つ。
        /// 時刻を見ずに配置だけで対を作ると、往復の2点が常に対になってしまい、
        /// <c>s</c> を付けた譜面で水平に立つはずのノーツが移動の軸に寝てしまう。
        /// 本体はずれが大きすぎると当てない。その境目が 40 度。
        /// </remarks>
        private static void ApplyAngleOffsets(List<NoteFigure> notes)
        {
            const double Limit = 40.0;

            foreach (int hand in new[] { 0, 1 })
            {
                var hands = notes.Where(n => n.ColorType == hand).ToList();

                foreach (var note in hands)
                {
                    double arrowX, arrowY;
                    if (!SwingSides.ArrowOf(note.CutDirection, out arrowX, out arrowY)) continue;

                    // 画面は下が正
                    note.Tilt = ToDegrees(arrowX, -arrowY);

                    var row = hands.Where(other => other != note && SameRow(note, other)).ToList();
                    if (row.Count != 1) continue;
                    var partner = row[0];

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

        private static double Wrap(double degrees)
        {
            while (degrees > 180) degrees -= 360;
            while (degrees < -180) degrees += 360;
            return degrees;
        }

        /// <summary>同じ拍に並んでいるか。本体が1拍ぶんを切り出すのに合わせた幅。</summary>
        private static bool SameRow(NoteFigure a, NoteFigure b)
        {
            const float Window = 0.01f;

            foreach (var one in a.Times)
                foreach (var other in b.Times)
                    if (Math.Abs(one - other) <= Window) return true;

            return false;
        }

        /// <summary>局所の上向き (0,-1) をこの向き（画面座標、下が正）へ回す角度（度）。</summary>
        private static double ToDegrees(double x, double y)
        {
            return Math.Atan2(x, -y) * 180.0 / Math.PI;
        }

        /// <summary>
        /// ノーツをプレイヤーの方へ向ける（本体の <c>NoteJump::_rotateTowardsPlayer</c>）。
        /// 面が正面を向いていないと、矢印の見かけの角度が変わる。外側の列ほど大きく回る。
        /// 向きが決まったら、切った線と切った瞬間の中心もここで作る（切った線は面の座標で持つ）。
        /// </summary>
        private static void ApplyLookAtPlayer(Replay replay, List<NoteFigure> notes)
        {
            var head = HeadAtCuts(replay);

            foreach (var note in notes)
            {
                // 回る前の「上」＝矢印の向き。傾きから戻す
                double radians = (note.Tilt ?? 0.0) * Math.PI / 180.0;
                var baseUp = new Vector3 { X = (float)Math.Sin(radians), Y = (float)Math.Cos(radians), Z = 0f };
                var center = new Vector3 { X = (float)note.CenterX, Y = (float)note.CenterY, Z = note.Depth };

                Vector3 right, up;
                NotePose.LookAtPlayer(center, head, baseUp, out right, out up);

                note.RightX = right.X;
                note.RightY = right.Y;
                note.UpX = up.X;
                note.UpY = up.Y;

                BuildCutLines(note, right, up);
                FindCutCenter(note);
            }
        }

        // ───────── 切った線・切った瞬間の中心 ─────────

        /// <summary>
        /// 切った瞬間の前後の、先端の通り道。弧長でノーツの幅ぶん（前後 <see cref="NoteSize"/> / 2 ずつ）を等間隔に取り、
        /// 切った瞬間の先端からのずれ（世界座標）で返す。真ん中の点は原点。
        /// 前後 100 ms で幅に届かなければ、届いた所で止める（端は同じ点が続く）。
        /// </summary>
        private static Vector3[] TipShape(Replay replay, ReplayNote note)
        {
            float moment = CutMoments.Find(replay, note);
            int saber = note.Cut.SaberType;
            Vector3 hilt, origin;
            CutMoments.BladeAt(replay, saber, moment, out hilt, out origin);

            const float Half = NoteSize / 2;
            var ahead = Walk(replay, saber, moment, origin, +1, Half);
            var behind = Walk(replay, saber, moment, origin, -1, Half);

            var shape = new Vector3[CutLineSamples];
            int middle = CutLineSamples / 2;
            for (int k = 0; k < CutLineSamples; k++)
            {
                float target = Half * (k - middle) / middle;
                var p = target >= 0 ? At(ahead, target) : At(behind, -target);
                shape[k] = new Vector3 { X = p.X - origin.X, Y = p.Y - origin.Y, Z = p.Z - origin.Z };
            }
            return shape;
        }

        /// <summary>切った瞬間から 1 ms ずつ進めて（戻して）、先端の道のりを弧長と組で集める。</summary>
        private static List<KeyValuePair<float, Vector3>> Walk(Replay replay, int saber, float moment, Vector3 origin, int direction, float length)
        {
            var points = new List<KeyValuePair<float, Vector3>> { new KeyValuePair<float, Vector3>(0f, origin) };
            float arc = 0f;
            var last = origin;
            for (int ms = 1; ms <= 100 && arc < length; ms++)
            {
                Vector3 hilt, tip;
                CutMoments.BladeAt(replay, saber, moment + direction * ms / 1000f, out hilt, out tip);
                arc += (float)Vector3.Distance(last, tip);
                points.Add(new KeyValuePair<float, Vector3>(arc, tip));
                last = tip;
            }
            return points;
        }

        /// <summary>弧長 <paramref name="arc"/> の位置。道のりより先なら終わりの点。</summary>
        private static Vector3 At(List<KeyValuePair<float, Vector3>> points, float arc)
        {
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].Key < arc) continue;
                float span = points[i].Key - points[i - 1].Key;
                float t = span > 1e-6f ? (arc - points[i - 1].Key) / span : 0f;
                return CutMoments.Lerp(points[i - 1].Value, points[i].Value, t);
            }
            return points[points.Count - 1].Value;
        }

        private static double Square(double x) { return x * x; }

        private static bool HasPoint(NoteCutInfo cut)
        {
            var p = cut.CutPoint;
            return p.X != 0 || p.Y != 0 || p.Z != 0;
        }

        /// <summary>
        /// 中心から見た打点の向き。打点 − 中心 ＝ 返り値 × 法線 × 距離。
        /// 中心は「打点 ± 法線 × 距離」の2つの候補のうち、本体の格子に近い方。
        /// 法線は中心の側を向いているとは限らない（実測で逆向きの回が混ざり、片方に決め打ちすると
        /// 中心がマスから 0.3〜0.6 m 横にずれる回が出た）。
        /// </summary>
        private static double PointSide(NoteFigure note, NoteCutInfo cut)
        {
            var normal = cut.CutNormal;
            double dist = cut.CutDistanceToCenter;
            var point = cut.CutPoint;
            double plus = Square(point.X + normal.X * dist - note.GridX) + Square(point.Y + normal.Y * dist - note.GridY);
            double minus = Square(point.X - normal.X * dist - note.GridX) + Square(point.Y - normal.Y * dist - note.GridY);
            return plus <= minus ? -1.0 : 1.0;
        }

        /// <summary>
        /// 切った線を、ノーツの面の座標で作る。
        ///
        /// 線は切った瞬間の前後の先端の通り道（ノーツの幅ぶん）を面に落とした曲線で、
        /// 振った向き（<c>saberDir</c>）に直交する向きへ、中心から <c>cutDistanceToCenter</c> だけずらして置く。
        /// ずらす側は、中心から見た打点の側（<see cref="PointSide"/>）。
        ///
        /// 面の座標で測るのは、外側の列のノーツがプレイヤーの方へ大きく回っているため。
        /// 振った向きを正面の平面へそのまま落とすと 30〜40° 傾いて見え、本体の角度ずれ（面の上で数度）と合わなかった。
        /// 切り口の面とノーツの面が交わる線も、面どうしが斜めなので傾き、ずれも距離より大きく見えた。
        /// 点数（中心の 15 点）に効くのは距離なので、それをそのまま幅にする。
        /// 打点が入っていない回（作ったリプレイなど）は、どちらへずらすか分からないので描かない。
        /// </summary>
        private static void BuildCutLines(NoteFigure note, Vector3 right, Vector3 up)
        {
            note.Cuts.Clear();
            for (int index = 0; index < note.RawCuts.Count; index++)
            {
                var cut = note.RawCuts[index];
                if (!HasPoint(cut)) continue;

                var swung = cut.SaberDirection;
                double u = swung.X * right.X + swung.Y * right.Y + swung.Z * right.Z;
                double v = swung.X * up.X + swung.Y * up.Y + swung.Z * up.Z;
                double length = Math.Sqrt(u * u + v * v);
                if (length < 1e-6) continue;

                double alongU = u / length, alongV = v / length;
                double acrossU = -alongV, acrossV = alongU;

                // 面の上の横向きを世界座標に戻して、中心から打点への向きと比べる
                double ax = right.X * acrossU + up.X * acrossV;
                double ay = right.Y * acrossU + up.Y * acrossV;
                double az = right.Z * acrossU + up.Z * acrossV;
                var normal = cut.CutNormal;
                double side = PointSide(note, cut) * (normal.X * ax + normal.Y * ay + normal.Z * az) >= 0 ? 1.0 : -1.0;
                double d = cut.CutDistanceToCenter * side;

                var shape = note.RawShapes[index];
                var points = new double[shape.Length][];
                for (int k = 0; k < shape.Length; k++)
                {
                    var q = shape[k];
                    points[k] = new[]
                    {
                        acrossU * d + (q.X * right.X + q.Y * right.Y + q.Z * right.Z),
                        acrossV * d + (q.X * up.X + q.Y * up.Y + q.Z * up.Z),
                    };
                }
                note.Cuts.Add(points);
            }
        }

        /// <summary>切った瞬間のノーツの中心を、切った回の平均で出す（中心の候補の選び方は <see cref="PointSide"/>）。</summary>
        private static void FindCutCenter(NoteFigure note)
        {
            double x = 0, y = 0, z = 0;
            int count = 0;
            foreach (var cut in note.RawCuts)
            {
                if (!HasPoint(cut)) continue;

                var point = cut.CutPoint;
                var normal = cut.CutNormal;
                double dist = cut.CutDistanceToCenter;
                double sign = -PointSide(note, cut);   // 中心 ＝ 打点 − 返り値 × 法線 × 距離
                x += point.X + sign * normal.X * dist;
                y += point.Y + sign * normal.Y * dist;
                z += point.Z + sign * normal.Z * dist;
                count++;
            }
            if (count == 0) return;

            note.CutCenter = new Vector3 { X = (float)(x / count), Y = (float)(y / count), Z = (float)(z / count) };
            note.HasCutCenter = true;
        }
    }
}
