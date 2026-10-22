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

        /// <summary>プレイヤーを向いた後の面の右・上・前（世界座標、長さ 1）。3D の立方体の軸にも使う。</summary>
        public Vector3 FaceRight { get; internal set; } = new Vector3 { X = 1f };
        public Vector3 FaceUp { get; internal set; } = new Vector3 { Y = 1f };
        public Vector3 FaceForward { get; internal set; } = new Vector3 { Z = 1f };

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

        /// <summary>
        /// 1ノーツの点数（115 点満点、<see cref="NoteScore.Total"/>）の、良い切りの平均。切っていなければ NaN。
        /// </summary>
        public double MeanScore
        {
            get { return RawCuts.Count == 0 ? double.NaN : RawCuts.Average(c => (double)NoteScore.Total(c)); }
        }

        /// <summary>振りかぶりの点（70 点満点）の平均。切っていなければ NaN。</summary>
        public double MeanBeforeCut
        {
            get { return RawCuts.Count == 0 ? double.NaN : RawCuts.Average(c => (double)NoteScore.BeforeCut(c)); }
        }

        /// <summary>振り抜きの点（30 点満点）の平均。切っていなければ NaN。</summary>
        public double MeanAfterCut
        {
            get { return RawCuts.Count == 0 ? double.NaN : RawCuts.Average(c => (double)NoteScore.AfterCut(c)); }
        }

        /// <summary>中心点（15 点満点）の平均。切っていなければ NaN。</summary>
        public double MeanCutDistance
        {
            get { return RawCuts.Count == 0 ? double.NaN : RawCuts.Average(c => (double)NoteScore.CutDistance(c)); }
        }

        /// <summary>
        /// 切った瞬間の刃の先端（平均、世界座標）。軌道の白丸と同じ点。切っていなければ <see cref="HasCutTip"/> が false。
        /// 先端がノーツより奥を通ったか（<see cref="TipBehind"/>）を見るのに使う。
        /// </summary>
        public Vector3 CutTip { get; internal set; }
        public bool HasCutTip { get; internal set; }

        /// <summary>
        /// 切った瞬間の刃の先端が、ノーツの中心より奥（プレイヤーから遠い側）を通ったか。
        /// 正面の図では、このノーツを軌道の上に重ねて描く（先端はノーツの向こうを通っているので）。
        /// </summary>
        public bool TipBehind { get { return HasCutTip && HasCutCenter && CutTip.Z > CutCenter.Z; } }

        /// <summary>
        /// 打点（面の座標、m）。切った線（中心の点を測る平面とノーツの面が交わる線）の平均の上で、ノーツの中心に一番近い点。
        /// 中心からここまでの長さが、中心の点に効くずれを面に落としたもの。切った線が無ければ false。
        /// </summary>
        public bool TryContact(out double u, out double v)
        {
            u = v = 0;
            if (Cuts.Count == 0) return false;
            var mean = NoteFigures.MeanCut(Cuts);
            var a = mean[0];
            var b = mean[mean.Length - 1];
            double du = b[0] - a[0], dv = b[1] - a[1];
            double length2 = du * du + dv * dv;
            double t = length2 < 1e-12 ? 0.0 : -(a[0] * du + a[1] * dv) / length2;   // 直線の上で原点（中心）に一番近い所
            u = a[0] + du * t;
            v = a[1] + dv * t;
            return true;
        }

        /// <summary>
        /// 刃がヒットボックスに当たった所（平均、世界座標）。切った判定のフレームで刃が掃いた三角形
        /// （今の刃の元・先と1つ前の刃の真ん中）がヒットボックスと重なった回について、そのフレームの刃のうち
        /// ヒットボックスの中に入っている部分の真ん中（入っていなければ箱の中心に一番近い所）。刃（<see cref="HitHilt"/>〜<see cref="HitTip"/>）の上にある。
        /// 中心の点は、この三角形を広げた平面で測る。求められなければ <see cref="HasHitPoint"/> が false。
        /// </summary>
        public Vector3 HitPoint { get; internal set; }
        public bool HasHitPoint { get; internal set; }

        /// <summary>ヒットボックスに当たったフレームの刃（元と先、平均、世界座標）。<see cref="HitPoint"/> と同じ回から。</summary>
        public Vector3 HitHilt { get; internal set; }
        public Vector3 HitTip { get; internal set; }

        /// <summary>
        /// 本体の打点（記録の cutPoint の平均、世界座標）。刃が通った面（扇を広げた平面）の上で、ノーツの中心にいちばん近い点。
        /// 中心からここまでの長さが中心の 15 点に効く距離。打点が入っていなければ false。
        /// </summary>
        public bool TryCutPoint(out Vector3 point)
        {
            point = new Vector3();
            var cuts = RawCuts.Where(NoteFigures.HasPoint).ToList();
            if (cuts.Count == 0) return false;
            point = new Vector3
            {
                X = cuts.Average(c => c.CutPoint.X),
                Y = cuts.Average(c => c.CutPoint.Y),
                Z = cuts.Average(c => c.CutPoint.Z),
            };
            return true;
        }


        /// <summary>
        /// 中心の点を測る平面（平均）。当たったフレームで刃が掃いた三角形を広げた平面で、記録の打点（cutPoint）を通り
        /// 記録の切った面の向き（cutNormal）に直角。打点が入っていなければ false。
        /// </summary>
        public bool TryCutPlane(out Vector3 point, out Vector3 normal)
        {
            normal = new Vector3();
            if (!TryCutPoint(out point)) return false;
            var cuts = RawCuts.Where(NoteFigures.HasPoint).ToList();
            var first = cuts[0].CutNormal;
            double x = 0, y = 0, z = 0;
            foreach (var c in cuts)
            {
                // 向きの正負をそろえてから平均する
                var n = c.CutNormal;
                float s = n.X * first.X + n.Y * first.Y + n.Z * first.Z < 0 ? -1f : 1f;
                x += s * n.X; y += s * n.Y; z += s * n.Z;
            }
            normal = NotePose.Normalize(new Vector3 { X = (float)x, Y = (float)y, Z = (float)z });
            return true;
        }

        /// <summary>刃がノーツに当たった所（世界座標）。<see cref="CutCenter"/> に面の座標を足したもの。</summary>
        public bool TryContactAtCut(out Vector3 contact)
        {
            contact = new Vector3();
            double u, v;
            if (!HasCutCenter || !TryContact(out u, out v)) return false;
            var c = CutCenter;
            contact = new Vector3
            {
                X = (float)(c.X + u * FaceRight.X + v * FaceUp.X),
                Y = (float)(c.Y + u * FaceRight.Y + v * FaceUp.Y),
                Z = (float)(c.Z + u * FaceRight.Z + v * FaceUp.Z),
            };
            return true;
        }

        /// <summary>フォアか（<see cref="SwingSides.NoteForehand"/>）。</summary>
        public bool Forehand { get { return SwingSides.NoteForehand(CutDirection, ColorType); } }

        // 作る途中だけ使う
        internal readonly List<float> Times = new List<float>();
        internal readonly List<NoteCutInfo> RawCuts = new List<NoteCutInfo>();
        internal readonly List<ReplayNote> RawNotes = new List<ReplayNote>();
        internal readonly List<Vector3> RawTips = new List<Vector3>();
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
        /// <param name="noteJumpSpeed">
        /// 譜面の NJS（<see cref="NoteFlight.NoteJumpSpeedOf"/>）。分かれば、ノーツの向きを本体と同じ計算（<see cref="NoteFlight"/>）で出す。
        /// 分からなければ（譜面が見つからない）、切った位置から頭の方を向き切った向きで近似する。
        /// </param>
        public static List<NoteFigure> Build(Replay replay, bool anchorToCuts = false, float? noteJumpSpeed = null)
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

                foreach (var played in replay.Notes)
                {
                    if (played.ColorType != placement.ColorType || played.LineIndex != placement.LineIndex ||
                        played.LineLayer != placement.LineLayer || played.CutDirection != placement.CutDirection) continue;

                    note.Times.Add(played.EventTime);
                    if (played.EventType == NoteEventType.Good && played.Cut != null && !played.IsStandIn)
                    {
                        note.RawCuts.Add(played.Cut);
                        note.RawNotes.Add(played);
                        // 切った瞬間の刃の先端
                        Vector3 hilt, tip;
                        CutMoments.BladeAt(replay, played.Cut.SaberType, CutMoments.Find(replay, played), out hilt, out tip);
                        note.RawTips.Add(tip);
                    }
                }

                notes.Add(note);
            }

            ApplyAngleOffsets(notes);
            MergeOverlaps(notes);
            ApplyLookAtPlayer(replay, notes, noteJumpSpeed, grid.OffsetY);
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
        /// ノーツの向き（本体の <c>NoteJump</c>）。飛んでくる間、頭の方へ向きを変えていく。
        /// 外側の列ほど大きく回り、面が斜めを向くので、正面から見ると縮んで見え、矢印の見かけの角度も変わる。
        /// 向きが決まったら、切った線と切った瞬間の中心もここで作る（切った線は面の座標で持つ）。
        /// </summary>
        /// <remarks>
        /// NJS が分かれば、切った回ごとに本体と同じ計算（<see cref="NoteFlight.TryRotationAtCut"/>）で切った瞬間の向きを出し、平均する。
        /// ただし、その向きで出した「切った向きのずれ」が記録（本体が切った瞬間のノーツの向きで出した値）と
        /// 近似より合わなければ使わない（譜面を作り直して NJS が変わった、など）。
        /// NJS が分からないか、出せなかった（ジャンプ距離が記録に無い・フレームが欠けている）ときは、
        /// 切った瞬間の位置から頭の方を向き切った向き（<see cref="NotePose.LookAtPlayer"/>）で近似する。
        /// </remarks>
        private static void ApplyLookAtPlayer(Replay replay, List<NoteFigure> notes, float? noteJumpSpeed, float jumpOffsetY)
        {
            var head = HeadAtCuts(replay);
            var simulated = noteJumpSpeed.HasValue
                ? Simulate(replay, notes, noteJumpSpeed.Value, jumpOffsetY)
                : new Dictionary<ReplayNote, Quat>();
            if (simulated.Count > 0 && !MatchesRecord(notes, simulated, head)) simulated.Clear();

            foreach (var note in notes)
            {
                Vector3 right, up, forward;

                var rotations = note.RawNotes.Where(simulated.ContainsKey).Select(n => simulated[n]).ToList();
                if (rotations.Count > 0)
                {
                    var q = Quat.Average(rotations);
                    right = q.Rotate(new Vector3 { X = -1f });
                    up = q.Rotate(new Vector3 { Y = -1f });
                    forward = q.Rotate(new Vector3 { Z = 1f });
                }
                else
                {
                    var center = new Vector3 { X = (float)note.CenterX, Y = (float)note.CenterY, Z = note.Depth };
                    NotePose.LookAtPlayer(center, head, BaseUp(note), out right, out up);
                    forward = NotePose.Cross(right, up);
                }

                note.RightX = right.X;
                note.RightY = right.Y;
                note.UpX = up.X;
                note.UpY = up.Y;
                note.FaceRight = right;
                note.FaceUp = up;
                note.FaceForward = forward;

                BuildCutLines(note, right, up);
                FindCutCenter(note);
                FindHitPoint(replay, note, right, up, forward);
                if (note.RawTips.Count > 0)
                {
                    note.CutTip = new Vector3
                    {
                        X = note.RawTips.Average(t => t.X),
                        Y = note.RawTips.Average(t => t.Y),
                        Z = note.RawTips.Average(t => t.Z),
                    };
                    note.HasCutTip = true;
                }
            }
        }

        // ノーツの模型は、回っていないとき矢印が下（−Y）、右が −X、奥が +Z を向く

        /// <summary>切った回ごとの、本体と同じ計算で出した切った瞬間の向き。出せなかった回は入らない。</summary>
        private static Dictionary<ReplayNote, Quat> Simulate(Replay replay, List<NoteFigure> notes, float noteJumpSpeed, float jumpOffsetY)
        {
            var result = new Dictionary<ReplayNote, Quat>();
            var spawns = NoteFlight.PlanSpawns(replay);
            foreach (var note in notes)
            {
                // 本体の endRotation（矢印の向き＋角度オフセット）。傾き（画面上、時計回り）とは 180 − 傾き の関係
                float endRotation = note.Tilt.HasValue ? (float)(180.0 - note.Tilt.Value) : 0f;
                foreach (var played in note.RawNotes)
                {
                    Quat rotation;
                    if (NoteFlight.TryRotationAtCut(replay, spawns, played, endRotation, noteJumpSpeed, jumpOffsetY, out rotation))
                        result[played] = rotation;
                }
            }
            return result;
        }

        /// <summary>
        /// 本体と同じ計算の向きが、近似の向きより記録に合っているか。
        /// 記録の「切った向きのずれ」は、本体が切った瞬間のノーツの向きで刃の向きを見たもの（<see cref="NoteFlight.CutDirDeviation"/>）。
        /// </summary>
        private static bool MatchesRecord(List<NoteFigure> notes, Dictionary<ReplayNote, Quat> simulated, Vector3 head)
        {
            double simulatedError = 0, approximateError = 0;
            int count = 0;
            foreach (var note in notes)
            {
                if (note.CutDirection == 8) continue;
                var baseUp = BaseUp(note);
                foreach (var played in note.RawNotes)
                {
                    Quat rotation;
                    if (played.Cut == null || !simulated.TryGetValue(played, out rotation)) continue;

                    Vector3 right, up;
                    NotePose.LookAtPlayer(played.Cut.CutPoint, head, baseUp, out right, out up);
                    var approximate = Quat.LookRotation(NotePose.Cross(right, up), new Vector3 { X = -up.X, Y = -up.Y, Z = -up.Z });

                    float recorded = played.Cut.CutDirDeviation;
                    simulatedError += AngleBetween(NoteFlight.CutDirDeviation(rotation, played.Cut.SaberDirection), recorded);
                    approximateError += AngleBetween(NoteFlight.CutDirDeviation(approximate, played.Cut.SaberDirection), recorded);
                    count++;
                }
            }
            return count == 0 || simulatedError <= approximateError;
        }

        private static double AngleBetween(double a, double b)
        {
            double d = Math.Abs(a - b) % 360.0;
            return d > 180.0 ? 360.0 - d : d;
        }

        /// <summary>回る前の「上」＝矢印の向き。傾きから戻す。</summary>
        private static Vector3 BaseUp(NoteFigure note)
        {
            double radians = (note.Tilt ?? 0.0) * Math.PI / 180.0;
            return new Vector3 { X = (float)Math.Sin(radians), Y = (float)Math.Cos(radians), Z = 0f };
        }

        // ───────── 切った線・切った瞬間の中心 ─────────

        private static double Square(double x) { return x * x; }

        internal static bool HasPoint(NoteCutInfo cut)
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
        /// 線は、中心の点を測る平面（ヒットボックスに当たったフレームで刃が掃いた三角形を広げた平面。記録の cutNormal に直角で
        /// 打点 cutPoint を通る）が、ノーツの中心を通る面（ノーツの正面と平行な面）と交わる直線。ノーツの四角の中だけを描く。
        /// 中心からこの線までの距離が、点数（中心の 15 点）に効く距離を面に落としたもの。
        /// 線の向きは振った向き（<c>saberDir</c> を面に落とした向き）にそろえ、先が振り抜いた側。
        /// 打点が入っていない回（作ったリプレイなど）は描かない。
        /// </summary>
        private static void BuildCutLines(NoteFigure note, Vector3 right, Vector3 up)
        {
            const double Half = NoteSize / 2;
            note.Cuts.Clear();
            for (int index = 0; index < note.RawCuts.Count; index++)
            {
                var cut = note.RawCuts[index];
                if (!HasPoint(cut)) continue;

                // 平面の向きを面の座標へ。面と平行（面の上に線が無い）なら描かない
                var n = cut.CutNormal;
                double nu = n.X * right.X + n.Y * right.Y + n.Z * right.Z;
                double nv = n.X * up.X + n.Y * up.Y + n.Z * up.Z;
                double nf2 = nu * nu + nv * nv;
                if (nf2 < 1e-8) continue;

                // その回のノーツの中心は、打点から平面の向きに距離ぶん戻した点。面の上の線は nu*u + nv*v = −sign*dist
                double sign = -PointSide(note, cut);
                double c = -sign * cut.CutDistanceToCenter;
                double qu = nu * c / nf2, qv = nv * c / nf2;      // 線の上で中心にいちばん近い点
                double nf = Math.Sqrt(nf2);
                double tu = -nv / nf, tv = nu / nf;               // 線の向き

                // 振った向きにそろえる
                var swung = cut.SaberDirection;
                double su = swung.X * right.X + swung.Y * right.Y + swung.Z * right.Z;
                double sv = swung.X * up.X + swung.Y * up.Y + swung.Z * up.Z;
                if (tu * su + tv * sv < 0) { tu = -tu; tv = -tv; }

                // ノーツの四角（±Half）の中だけ。外れていれば中心に近い点の前後 Half
                double from = -Half, to = Half;
                if (!ClipLine(qu, qv, tu, tv, Half, ref from, ref to)) { from = -Half; to = Half; }

                var points = new double[CutLineSamples][];
                for (int k = 0; k < CutLineSamples; k++)
                {
                    double t = from + (to - from) * k / (CutLineSamples - 1);
                    points[k] = new[] { qu + tu * t, qv + tv * t };
                }
                note.Cuts.Add(points);
            }
        }

        /// <summary>直線 q + t·d のうち、|u|,|v| ≤ half の四角に入る t の範囲。入らなければ false。</summary>
        private static bool ClipLine(double qu, double qv, double du, double dv, double half, ref double from, ref double to)
        {
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            foreach (var pair in new[] { new[] { qu, du }, new[] { qv, dv } })
            {
                double q = pair[0], d = pair[1];
                if (Math.Abs(d) < 1e-9)
                {
                    if (Math.Abs(q) > half) return false;
                    continue;
                }
                double t1 = (-half - q) / d, t2 = (half - q) / d;
                lo = Math.Max(lo, Math.Min(t1, t2));
                hi = Math.Min(hi, Math.Max(t1, t2));
            }
            if (lo >= hi) return false;
            from = lo;
            to = hi;
            return true;
        }

        /// <summary>
        /// 刃がヒットボックスに当たった所（<see cref="NoteFigure.HitPoint"/>）。本体が切った判定をしたフレーム
        /// （記録の時刻 <see cref="ReplayNote.EventTime"/>）の三角形を、その回のノーツの中心に置いたヒットボックスで切り取り、
        /// 残った形の真ん中を平均する。
        /// </summary>
        private static void FindHitPoint(Replay replay, NoteFigure note, Vector3 right, Vector3 up, Vector3 forward)
        {
            var frames = replay.Frames;
            var box = NoteBox.Hitbox;
            double hx = 0, hy = 0, hz = 0, tx = 0, ty = 0, tz = 0, along = 0;
            int count = 0;
            for (int index = 0; index < note.RawCuts.Count; index++)
            {
                var cut = note.RawCuts[index];
                if (!HasPoint(cut) || frames.Count < 2) continue;

                // 切った判定のフレーム（記録の時刻に一番近いフレーム）と、その1つ前
                float time = note.RawNotes[index].EventTime;
                int i = 1;
                while (i < frames.Count - 1 && frames[i].Time < time) i++;
                if (i > 1 && Math.Abs(frames[i - 1].Time - time) < Math.Abs(frames[i].Time - time)) i--;
                var now = frames[i].Saber(cut.SaberType);
                var before = frames[i - 1].Saber(cut.SaberType);
                var middle = new Vector3
                {
                    X = (before.Position.X + before.Tip.X) / 2f,
                    Y = (before.Position.Y + before.Tip.Y) / 2f,
                    Z = (before.Position.Z + before.Tip.Z) / 2f,
                };

                // その回のノーツの中心（打点から切った面の向きに距離ぶん戻した点）
                double sign = -PointSide(note, cut);
                var center = new Vector3
                {
                    X = (float)(cut.CutPoint.X + sign * cut.CutNormal.X * cut.CutDistanceToCenter),
                    Y = (float)(cut.CutPoint.Y + sign * cut.CutNormal.Y * cut.CutDistanceToCenter),
                    Z = (float)(cut.CutPoint.Z + sign * cut.CutNormal.Z * cut.CutDistanceToCenter),
                };
                var boxCenter = new Vector3
                {
                    X = center.X + box.Offset * forward.X,
                    Y = center.Y + box.Offset * forward.Y,
                    Z = center.Z + box.Offset * forward.Z,
                };

                var polygon = new List<Vector3> { now.Position, now.Tip, middle };
                var axes = new[] { right, up, forward };
                var halves = new[] { box.HalfWidth, box.HalfHeight, box.HalfDepth };
                for (int k = 0; k < 3 && polygon.Count >= 3; k++)
                    foreach (float s in new[] { 1f, -1f })
                    {
                        var a = axes[k];
                        float half = halves[k];
                        polygon = ClipPolygon(polygon, p => half - s * (a.X * (p.X - boxCenter.X) + a.Y * (p.Y - boxCenter.Y) + a.Z * (p.Z - boxCenter.Z)));
                        if (polygon.Count < 3) break;
                    }
                if (polygon.Count < 3) continue;   // 重ならない（記録の時刻と判定のフレームがずれた、など）

                // 刃（元→先）の上のどこか（0〜1）。ヒットボックスの中に入っている部分の真ん中、入っていなければ箱の中心に一番近い所
                along += BladeFraction(now.Position, now.Tip, boxCenter, axes, halves);
                hx += now.Position.X; hy += now.Position.Y; hz += now.Position.Z;
                tx += now.Tip.X; ty += now.Tip.Y; tz += now.Tip.Z;
                count++;
            }
            if (count == 0) return;
            // 刃の何割の所かを平均して、平均の刃の上に置く（描く白い線の上に必ず乗る）
            double f = along / count;
            note.HitPoint = new Vector3
            {
                X = (float)((hx + (tx - hx) * f) / count),
                Y = (float)((hy + (ty - hy) * f) / count),
                Z = (float)((hz + (tz - hz) * f) / count),
            };
            note.HitHilt = new Vector3 { X = (float)(hx / count), Y = (float)(hy / count), Z = (float)(hz / count) };
            note.HitTip = new Vector3 { X = (float)(tx / count), Y = (float)(ty / count), Z = (float)(tz / count) };
            note.HasHitPoint = true;
        }

        /// <summary>
        /// 刃（<paramref name="hilt"/> → <paramref name="tip"/>）のうち箱の中に入っている部分の真ん中が、刃の何割の所か（0〜1）。
        /// 入っていなければ、刃の上で箱の中心に一番近い所。
        /// </summary>
        private static double BladeFraction(Vector3 hilt, Vector3 tip, Vector3 boxCenter, Vector3[] axes, float[] halves)
        {
            var d = NotePose.Subtract(tip, hilt);
            var o = NotePose.Subtract(hilt, boxCenter);
            double lo = 0, hi = 1;
            bool inside = true;
            for (int k = 0; k < 3 && inside; k++)
            {
                var a = axes[k];
                double p0 = o.X * a.X + o.Y * a.Y + o.Z * a.Z;
                double dp = d.X * a.X + d.Y * a.Y + d.Z * a.Z;
                if (Math.Abs(dp) < 1e-9)
                {
                    if (Math.Abs(p0) > halves[k]) inside = false;
                    continue;
                }
                double t1 = (-halves[k] - p0) / dp, t2 = (halves[k] - p0) / dp;
                lo = Math.Max(lo, Math.Min(t1, t2));
                hi = Math.Min(hi, Math.Max(t1, t2));
                if (lo > hi) inside = false;
            }
            if (inside) return (lo + hi) / 2;

            double length2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
            if (length2 < 1e-12) return 0;
            double t = -(o.X * d.X + o.Y * d.Y + o.Z * d.Z) / length2;
            return Math.Max(0, Math.Min(1, t));
        }

        /// <summary>多角形のうち <paramref name="inside"/> が 0 以上の側だけを残す（Sutherland–Hodgman の1枚ぶん）。</summary>
        private static List<Vector3> ClipPolygon(List<Vector3> polygon, Func<Vector3, float> inside)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                float da = inside(a), db = inside(b);
                if (da >= 0) result.Add(a);
                if ((da >= 0) != (db >= 0))
                {
                    float t = da / (da - db);
                    result.Add(new Vector3 { X = a.X + (b.X - a.X) * t, Y = a.Y + (b.Y - a.Y) * t, Z = a.Z + (b.Z - a.Z) * t });
                }
            }
            return result;
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
