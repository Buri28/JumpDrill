using System;
using System.Collections.Generic;
using System.Linq;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 振りの図の中身。振りを遷移ごとに束ねたものと、その平均。GUI の正面・横・3D の図、MOD の図と VR 表示で同じものを使う。
    /// </summary>
    /// <remarks>
    /// 振りは「前に切った瞬間から次に切る瞬間まで」で、先端と手元を同じ瞬間どうしで並べる。
    /// 端は切った瞬間にそろえる（<see cref="CutMoments"/>、採点と同じ）。遷移ごとに束ねるのは、往路と復路を混ぜると
    /// 別物が重なって、束の太さが再現性を表さなくなるため。振りの決め方は数字と同じ（<see cref="SwingAnalyzer.CleanPairs"/>）。
    /// 3D に置くノーツは切った瞬間の中心の平均で、配置（手・マス・向き）ごとに1つ（<see cref="NoteFigures"/>）。
    /// 座標は本体と同じ（X が右、Y が上、Z が前、m）。
    /// </remarks>
    public sealed class SwingScene
    {
        /// <summary>ノーツの一辺 (m)。</summary>
        public const float NoteSize = NoteFigures.NoteSize;

        /// <summary>1本の振り（または遷移ごとの平均）。先端と手元を同じ瞬間どうしで並べたもの。</summary>
        public sealed class Swing
        {
            public int SaberType { get; internal set; }
            public Vector3[] Tips { get; internal set; }
            public Vector3[] Hilts { get; internal set; }

            /// <summary>その遷移の平均の振りからのずれ (m)。1本1本の振りの濃さに使う。平均の振りでは 0。</summary>
            public double Deviation { get; internal set; }

            /// <summary>次に切るノーツ・前に切ったノーツがフォア（切り下げ）か（採点と同じ決め方）。</summary>
            public bool ToForehand { get; internal set; }
            public bool FromForehand { get; internal set; }

            /// <summary>次に切るノーツの矢印の向き（X-Y、上が正）。</summary>
            public double DirX { get; internal set; }
            public double DirY { get; internal set; }

            /// <summary>i 番目の区間（i-1 → i）がフォアか。折り返し点で分ける（<see cref="SwingSides"/>）。</summary>
            public bool SegmentForehand(int i)
            {
                i = Math.Max(1, Math.Min(Tips.Length - 1, i));
                return SwingSides.SegmentForehand(i, SwingSides.TurnIndex(Tips, DirX, DirY), ToForehand, FromForehand);
            }
        }

        /// <summary>切った瞬間のノーツ（平均）。</summary>
        public sealed class Note
        {
            public int ColorType { get; internal set; }
            public int CutDirection { get; internal set; }
            public bool Forehand { get; internal set; }
            public Vector3 Center { get; internal set; }

            /// <summary>
            /// 本体の打点（世界座標、<see cref="NoteFigure.TryCutPoint"/>）。刃が通った面を広げた平面の上で中心にいちばん近い点で、
            /// 中心からの長さが中心の 15 点に効く距離。分からなければ <see cref="HasContact"/> が false。
            /// </summary>
            public Vector3 Contact { get; internal set; }
            public bool HasContact { get; internal set; }

            /// <summary>中心の点を測る平面（<see cref="NoteFigure.TryCutPlane"/>）の、通る点と向き。</summary>
            public Vector3 PlanePoint { get; internal set; }
            public Vector3 PlaneNormal { get; internal set; }
            public bool HasPlane { get; internal set; }

            /// <summary>中心の点を測る平面で、箱（<paramref name="box"/>）を切った断面。平面が分からないか交わらなければ空。</summary>
            public List<Vector3> PlaneSection(NoteBox box)
            {
                return HasPlane ? box.PlaneSection(Center, AxisU, AxisV, AxisW, PlanePoint, PlaneNormal) : new List<Vector3>();
            }

            /// <summary>刃がヒットボックスに当たった所（<see cref="NoteFigure.HitPoint"/>）。中心の点はここで掃いた面を広げた平面で測る。</summary>
            public Vector3 HitPoint { get; internal set; }
            public bool HasHitPoint { get; internal set; }

            /// <summary>1ノーツの点数（115 点満点）の平均（<see cref="NoteFigure.MeanScore"/>）。切っていなければ NaN。</summary>
            public double MeanScore { get; internal set; } = double.NaN;

            /// <summary>振りかぶり（70）・振り抜き（30）・中心（15）の点の平均（<see cref="NoteFigure"/> と同じ）。切っていなければ NaN。</summary>
            public double MeanBeforeCut { get; internal set; } = double.NaN;
            public double MeanAfterCut { get; internal set; } = double.NaN;
            public double MeanCutDistance { get; internal set; } = double.NaN;

            /// <summary>
            /// 立方体の軸（長さ 1）。V が切る向き、U がその右、W が前（プレイヤーから遠ざかる向き）。
            /// 本体と同じく、ノーツはプレイヤー（頭）の方を向く（<see cref="NotePose.LookAtPlayer"/>）。
            /// 外側の列のノーツほど中央へ向けて大きく回る。傾きは角度オフセットを当てた後のもの（<see cref="NoteFigure.Tilt"/>）。
            /// </summary>
            public Vector3 AxisU { get; internal set; } = new Vector3 { X = 1f };
            public Vector3 AxisV { get; internal set; } = new Vector3 { Y = 1f };
            public Vector3 AxisW { get; internal set; } = new Vector3 { Z = 1f };

            /// <summary>面の上の (u, v) と前後 w（中心から、m）→ 世界座標。</summary>
            public Vector3 At(float u, float v, float w)
            {
                var au = AxisU;
                var av = AxisV;
                var aw = AxisW;
                return new Vector3
                {
                    X = Center.X + u * au.X + v * av.X + w * aw.X,
                    Y = Center.Y + u * au.Y + v * av.Y + w * aw.Y,
                    Z = Center.Z + u * au.Z + v * av.Z + w * aw.Z,
                };
            }

            /// <summary>箱の 8 隅。番号の 1・2・4 の桁が u・v・w の正負（0 が負）。</summary>
            public Vector3[] Corners(NoteBox box)
            {
                return box.Corners(Center, AxisU, AxisV, AxisW);
            }
        }

        /// <summary>1本1本の振り。</summary>
        public List<Swing> Swings { get; } = new List<Swing>();

        /// <summary>遷移ごとの平均の振り。</summary>
        public List<Swing> Means { get; } = new List<Swing>();

        public List<Note> Notes { get; } = new List<Note>();

        /// <summary>切った瞬間の頭の位置（平均）。プレイヤーがどこに立っていたかの目印。</summary>
        public Vector3 Head { get; private set; }
        public bool HasHead { get; private set; }

        /// <param name="replay">軌道に使うリプレイ（タワー・スタックは端の点だけにまとめたもの）。</param>
        /// <param name="notesFrom">ノーツを置くリプレイ（まとめる前）。null なら <paramref name="replay"/>。</param>
        /// <param name="withNotes">3D に置くノーツと頭の位置も作るか。正面・横の図は振りだけ使う（ノーツは <see cref="NoteFigures"/>）。</param>
        /// <param name="noteJumpSpeed">譜面の NJS。分かればノーツの向きを本体と同じ計算で出す（<see cref="NoteFigures.Build"/>）。</param>
        public static SwingScene Build(Replay replay, Replay notesFrom = null, bool withNotes = true, float? noteJumpSpeed = null)
        {
            var scene = new SwingScene();
            if (replay == null) return scene;

            foreach (int saberType in new[] { 0, 1 }) scene.LoadHand(replay, saberType);
            if (withNotes)
            {
                scene.LoadNotes(NoteFigures.Build(notesFrom ?? replay, noteJumpSpeed: noteJumpSpeed));
                scene.LoadHead(notesFrom ?? replay);
            }
            return scene;
        }

        /// <summary>遷移ごとに振りを束ね、平均を取る（図と同じ決め方）。</summary>
        private void LoadHand(Replay replay, int saberType)
        {
            var groups = new Dictionary<string, List<Swing>>();
            var sequence = SwingAnalyzer.HandSequence(replay, saberType);
            var order = new Dictionary<ReplayNote, int>();
            for (int i = 0; i < sequence.Count; i++) order[sequence[i]] = i;

            foreach (var pair in SwingAnalyzer.CleanPairs(sequence, saberType))
            {
                var swing = Extract(replay, saberType, CutMoments.Find(replay, pair.Key), CutMoments.Find(replay, pair.Value));
                if (swing == null) continue;

                double dirX, dirY;
                SwingSides.ArrowOf(pair.Value.CutDirection, out dirX, out dirY);
                swing.ToForehand = SwingAnalyzer.IsForehand(pair.Value, order[pair.Value]);
                swing.FromForehand = SwingAnalyzer.IsForehand(pair.Key, order[pair.Key]);
                swing.DirX = dirX;
                swing.DirY = dirY;

                string key = pair.Key.LineIndex + "," + pair.Key.LineLayer + ">" + pair.Value.LineIndex + "," + pair.Value.LineLayer;
                List<Swing> list;
                if (!groups.TryGetValue(key, out list)) groups[key] = list = new List<Swing>();
                list.Add(swing);
            }

            foreach (var group in groups.Values)
            {
                if (group.Count < 2) continue;

                var mean = new Swing
                {
                    SaberType = saberType,
                    ToForehand = group[0].ToForehand,
                    FromForehand = group[0].FromForehand,
                    DirX = group[0].DirX,
                    DirY = group[0].DirY,
                    Tips = Mean(group.Select(s => s.Tips).ToList()),
                    Hilts = Mean(group.Select(s => s.Hilts).ToList()),
                };
                foreach (var swing in group)
                {
                    double sum = 0;
                    for (int i = 0; i < swing.Tips.Length; i++) sum += Vector3.Distance(swing.Tips[i], mean.Tips[i]);
                    swing.Deviation = sum / swing.Tips.Length;
                }

                Swings.AddRange(group);
                Means.Add(mean);
            }
        }

        /// <summary>
        /// 切った瞬間から次に切った瞬間までの刃。先端の弧長で等間隔に取り、手元も同じ瞬間で取る。
        /// </summary>
        private static Swing Extract(Replay replay, int saberType, float from, float to)
        {
            var tips = new List<Vector3>();
            var hilts = new List<Vector3>();

            Vector3 hilt, tip;
            CutMoments.BladeAt(replay, saberType, from, out hilt, out tip);
            tips.Add(tip);
            hilts.Add(hilt);
            foreach (var frame in replay.Frames)
            {
                if (frame.Time <= from) continue;
                if (frame.Time >= to) break;
                tips.Add(frame.SaberTip(saberType));
                hilts.Add(frame.Saber(saberType).Position);
            }
            CutMoments.BladeAt(replay, saberType, to, out hilt, out tip);
            tips.Add(tip);
            hilts.Add(hilt);
            if (tips.Count < 4) return null;

            // 先端の弧長で取り直し、手元も同じ位置（同じ瞬間）で取る
            int samples = SwingAnalyzer.SamplesPerSwing;
            var cumulative = new double[tips.Count];
            for (int i = 1; i < tips.Count; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(tips[i - 1], tips[i]);
            double total = cumulative[tips.Count - 1];

            var outTips = new Vector3[samples];
            var outHilts = new Vector3[samples];
            int cursor = 0;
            for (int i = 0; i < samples; i++)
            {
                double target = total * i / (samples - 1);
                while (cursor < tips.Count - 2 && cumulative[cursor + 1] < target) cursor++;
                double segment = cumulative[cursor + 1] - cumulative[cursor];
                float t = segment <= 1e-9 ? 0f : (float)((target - cumulative[cursor]) / segment);
                outTips[i] = CutMoments.Lerp(tips[cursor], tips[cursor + 1], t);
                outHilts[i] = CutMoments.Lerp(hilts[cursor], hilts[cursor + 1], t);
            }

            return new Swing { SaberType = saberType, Tips = outTips, Hilts = outHilts };
        }

        private static Vector3[] Mean(List<Vector3[]> paths)
        {
            int samples = paths[0].Length;
            var mean = new Vector3[samples];
            for (int i = 0; i < samples; i++)
                mean[i] = new Vector3
                {
                    X = paths.Average(p => p[i].X),
                    Y = paths.Average(p => p[i].Y),
                    Z = paths.Average(p => p[i].Z),
                };
            return mean;
        }

        /// <summary>
        /// 3D に置くノーツ。図のノーツ（<see cref="NoteFigures"/>）の、切った瞬間の中心に置く。
        /// 向きは図と同じもの（頭の方を向いた面の軸、<see cref="NoteFigure.FaceRight"/> など）で、正面図の矢印・切った線とそろう。
        /// </summary>
        private void LoadNotes(List<NoteFigure> figures)
        {
            foreach (var figure in figures)
            {
                if (!figure.HasCutCenter) continue;

                Vector3 contact, planePoint, planeNormal;
                bool hasContact = figure.TryCutPoint(out contact);
                bool hasPlane = figure.TryCutPlane(out planePoint, out planeNormal);
                Notes.Add(new Note
                {
                    ColorType = figure.ColorType,
                    CutDirection = figure.CutDirection,
                    Forehand = figure.Forehand,
                    Center = figure.CutCenter,
                    AxisU = figure.FaceRight,
                    AxisV = figure.FaceUp,
                    AxisW = figure.FaceForward,
                    MeanScore = figure.MeanScore,
                    MeanBeforeCut = figure.MeanBeforeCut,
                    MeanAfterCut = figure.MeanAfterCut,
                    MeanCutDistance = figure.MeanCutDistance,
                    Contact = contact,
                    HasContact = hasContact,
                    PlanePoint = planePoint,
                    PlaneNormal = planeNormal,
                    HasPlane = hasPlane,
                    HitPoint = figure.HitPoint,
                    HasHitPoint = figure.HasHitPoint,
                });
            }
        }

        /// <summary>頭の位置（<see cref="NoteFigures.HeadAtCuts"/>、図のノーツの向きを出すのと同じもの）。</summary>
        private void LoadHead(Replay replay)
        {
            if (!replay.Notes.Any(n => n.EventType == NoteEventType.Good && n.Cut != null) || replay.Frames.Count == 0) return;
            Head = NoteFigures.HeadAtCuts(replay);
            HasHead = true;
        }
    }
}
