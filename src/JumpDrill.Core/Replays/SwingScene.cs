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
            /// 立方体の軸（長さ 1）。V が切る向き、U がその右、W が前（プレイヤーから遠ざかる向き）。
            /// 矢印の 8 方向だけで回し、プレイヤーの方へ向く分は入れない。
            /// </summary>
            public Vector3 AxisU
            {
                get { double a = Turn; return new Vector3 { X = (float)Math.Cos(a), Y = (float)-Math.Sin(a), Z = 0f }; }
            }

            public Vector3 AxisV
            {
                get { double a = Turn; return new Vector3 { X = (float)Math.Sin(a), Y = (float)Math.Cos(a), Z = 0f }; }
            }

            public Vector3 AxisW { get { return new Vector3 { Z = 1f }; } }

            private double Turn
            {
                get { double a = SwingSides.ArrowAngle(CutDirection); return double.IsNaN(a) ? 0.0 : a; }
            }

            /// <summary>面の上の (u, v) と前後 w（中心から、m）→ 世界座標。</summary>
            public Vector3 At(float u, float v, float w)
            {
                var au = AxisU;
                var av = AxisV;
                return new Vector3
                {
                    X = Center.X + u * au.X + v * av.X,
                    Y = Center.Y + u * au.Y + v * av.Y,
                    Z = Center.Z + w,
                };
            }

            /// <summary>立方体の 8 隅。番号の 1・2・4 の桁が u・v・w の正負（0 が負）。</summary>
            public Vector3[] Corners()
            {
                float h = NoteSize / 2;
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    corners[i] = At((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h);
                return corners;
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
        public static SwingScene Build(Replay replay, Replay notesFrom = null, bool withNotes = true)
        {
            var scene = new SwingScene();
            if (replay == null) return scene;

            foreach (int saberType in new[] { 0, 1 }) scene.LoadHand(replay, saberType);
            if (withNotes)
            {
                scene.LoadNotes(NoteFigures.Build(notesFrom ?? replay));
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

        /// <summary>3D に置くノーツ。図のノーツ（<see cref="NoteFigures"/>）の、切った瞬間の中心。</summary>
        private void LoadNotes(List<NoteFigure> figures)
        {
            foreach (var figure in figures)
            {
                if (!figure.HasCutCenter) continue;
                Notes.Add(new Note
                {
                    ColorType = figure.ColorType,
                    CutDirection = figure.CutDirection,
                    Forehand = figure.Forehand,
                    Center = figure.CutCenter,
                });
            }
        }

        private void LoadHead(Replay replay)
        {
            var cuts = replay.Notes.Where(n => n.EventType == NoteEventType.Good && n.Cut != null).ToList();
            if (cuts.Count == 0 || replay.Frames.Count == 0) return;

            double x = 0, y = 0, z = 0;
            foreach (var note in cuts)
            {
                int i = replay.Frames.FindIndex(f => f.Time >= note.EventTime);
                var head = replay.Frames[i < 0 ? replay.Frames.Count - 1 : i].Head.Position;
                x += head.X; y += head.Y; z += head.Z;
            }
            Head = new Vector3 { X = (float)(x / cuts.Count), Y = (float)(y / cuts.Count), Z = (float)(z / cuts.Count) };
            HasHead = true;
        }

        /// <summary>
        /// 振りの刃が通った面（帯）のうち、ノーツの立方体の中を通った部分。
        /// 帯の区間ごとの四角を立方体の 6 枚の面で順に切り取ったもの（凸な多角形の切り取り）。
        /// 区間の番号（i-1 → i の i）と多角形の組で返す。
        /// </summary>
        public static List<KeyValuePair<int, Vector3[]>> InsideNote(Swing swing, Note note)
        {
            var result = new List<KeyValuePair<int, Vector3[]>>();
            var axes = new[] { note.AxisU, note.AxisV, note.AxisW };
            var center = note.Center;

            for (int i = 1; i < swing.Tips.Length; i++)
            {
                var polygon = new List<Vector3> { swing.Hilts[i - 1], swing.Tips[i - 1], swing.Tips[i], swing.Hilts[i] };
                foreach (var axis in axes)
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        // 中心からの、この軸の向きの距離が NoteSize / 2 以下の側を残す
                        var a = axis;
                        float s = sign;
                        polygon = Clip(polygon, p => NoteSize / 2 - s * (a.X * (p.X - center.X) + a.Y * (p.Y - center.Y) + a.Z * (p.Z - center.Z)));
                        if (polygon.Count < 3) break;
                    }
                if (polygon.Count >= 3) result.Add(new KeyValuePair<int, Vector3[]>(i, polygon.ToArray()));
            }
            return result;
        }

        /// <summary>多角形のうち <paramref name="inside"/> が 0 以上の側だけを残す（Sutherland–Hodgman の1枚ぶん）。</summary>
        private static List<Vector3> Clip(List<Vector3> polygon, Func<Vector3, float> inside)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                float da = inside(a), db = inside(b);
                if (da >= 0) result.Add(a);
                if ((da >= 0) != (db >= 0)) result.Add(CutMoments.Lerp(a, b, da / (da - db)));
            }
            return result;
        }
    }
}
