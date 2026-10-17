using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Model;

namespace JumpDrill.Replays
{
    /// <summary>
    /// タワー（とスタック）の譜面を、<b>端の点だけの普通のジャンプ</b>として読み直す。
    ///
    /// <c>3&gt;b</c> のタワーは 3 7 b を同じ時刻に同じ向きで切るが、振りとしては
    /// 3 から b への1本。再現性や倍率の数え方は端の2点だけのドリルと同じにしたいので、
    /// 同じ手・同じ時刻のノーツを<b>振りの行き先の1つ</b>にまとめる。
    ///
    /// <b>Acc（精度）はまとめる前のノーツで出す。</b>ゲームの Acc は全ノーツから出ているため。
    ///
    /// <b>ミスは切れた割合で数える。</b>2連（スタック）なら1つミスで振りの重みが 1/2、
    /// 3連（タワー）なら1つで 2/3、2つで 1/3 になる。全部ミスしたときだけ振りに数えない。
    /// 端を落としても間を切っていれば振りはあったので、残した端のノーツを「切れた」ものに置き換える
    /// （位置は端のまま、切った時刻と切り方は切れたノーツのもの）。
    /// </summary>
    public static class TowerReplay
    {
        /// <summary>同じ時刻とみなす幅 (秒)。</summary>
        private const float SameTime = 0.001f;

        /// <summary>
        /// その手のノーツの間隔（上位 1/4 の値）の何倍空いたら、前の行き先を当てにしないか。
        /// 片手ずつのブロックの切れ目（カウントインぶん空く）では、循環が最初の点からやり直しになる。
        /// 秒で決め打ちすると、遅いドリルで毎回やり直しと見なしてしまう。
        /// 中央値にしないのは、間隔が2種類交互に来る譜面（フリックの短い・長い、往復ごとに交互の 1 間隔・3 間隔）で
        /// 短い方が中央値になり、長い方の間隔までブロックの切れ目と見なしてしまうため。
        /// </summary>
        private const float BlockGapFactor = 2.5f;

        /// <summary>
        /// まとめたリプレイ。まとめるものが無ければ同じインスタンスを返す。
        /// フレームと情報は元のものを共有する。
        /// </summary>
        public static Replay Collapse(Replay replay)
        {
            Dictionary<ReplayNote, ReplayNote> owners;
            return Collapse(replay, out owners);
        }

        /// <param name="owners">
        /// 元のノーツ（爆弾以外）から、それが属する振りの残したノーツへの対応。
        /// タワーの間のノーツのミスを、どちらの振り（フォア／バック）のミスか数えるのに使う。
        /// まとめるものが無ければ null（どのノーツも自分自身）。
        /// </param>
        public static Replay Collapse(Replay replay, out Dictionary<ReplayNote, ReplayNote> owners)
        {
            Dictionary<ReplayNote, double> shares;
            return Collapse(replay, out owners, out shares);
        }

        /// <param name="shares">
        /// まとめた振り（残したノーツ）ごとの、切れたノーツの割合（0〜1）。2つ以上まとめたものだけ入る。
        /// 振りの重みに掛ける。まとめるものが無ければ null。
        /// </param>
        public static Replay Collapse(Replay replay, out Dictionary<ReplayNote, ReplayNote> owners,
            out Dictionary<ReplayNote, double> shares)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            owners = null;
            shares = null;
            if (!HasStacks(replay.Notes)) return replay;

            owners = new Dictionary<ReplayNote, ReplayNote>();
            shares = new Dictionary<ReplayNote, double>();

            var hints = FirstTargets(replay.Info != null ? replay.Info.SongName : null);
            var keep = new HashSet<ReplayNote>();

            // 端を落として間を切ったときの、端の代わりのノーツ（StandIn）
            var standIns = new Dictionary<ReplayNote, ReplayNote>();

            foreach (var hand in new[] { 0, 1 })
            {
                var notes = replay.Notes
                    .Where(n => n.EventType != NoteEventType.Bomb && n.ColorType == hand)
                    .OrderBy(n => n.SpawnTime)
                    .ToList();

                GridPosition? hint;
                hints.TryGetValue(hand, out hint);

                var groups = Groups(notes).ToList();
                float blockGap = TypicalGap(groups) * BlockGapFactor;

                GridPosition? previous = null;
                float previousTime = float.NegativeInfinity;

                foreach (var group in groups)
                {
                    float time = group[0].SpawnTime;
                    if (time - previousTime > blockGap) previous = null;

                    var chosen = group.Count == 1 ? group[0] : Destination(group, previous, previous == null ? hint : null);
                    var position = PositionOf(chosen);
                    keep.Add(chosen);
                    if (group.Count > 1)
                    {
                        var standIn = StandIn(group, chosen, hand);
                        if (standIn != chosen) standIns[chosen] = standIn;
                        chosen = standIn;
                        shares[chosen] = (double)group.Count(n => IsCut(n, hand)) / group.Count;
                    }
                    foreach (var n in group) owners[n] = chosen;

                    previous = position;
                    previousTime = time;
                }
            }

            return new Replay
            {
                Path = replay.Path,
                Info = replay.Info,
                Frames = replay.Frames,
                WallHits = replay.WallHits,
                Notes = replay.Notes.Where(n => n.EventType == NoteEventType.Bomb || keep.Contains(n))
                    .Select(n => { ReplayNote s; return standIns.TryGetValue(n, out s) ? s : n; })
                    .ToList(),
            };
        }

        private static bool IsCut(ReplayNote note, int hand)
        {
            return note.EventType == NoteEventType.Good && note.Cut != null && note.Cut.SaberType == hand;
        }

        /// <summary>
        /// 残す端のノーツ。端を切れていれば端そのもの。端を落として間を切っていれば、
        /// 端の位置で、切れたノーツ（端に近いもの）の時刻と切り方を持った代わりのノーツ。全部落としていれば端のまま（ミス）。
        /// </summary>
        private static ReplayNote StandIn(List<ReplayNote> group, ReplayNote end, int hand)
        {
            if (IsCut(end, hand)) return end;

            var cut = group.Where(n => IsCut(n, hand))
                .OrderBy(n => Distance(PositionOf(n), PositionOf(end)))
                .FirstOrDefault();
            if (cut == null) return end;

            return new ReplayNote
            {
                NoteId = end.NoteId,
                SpawnTime = end.SpawnTime,
                EventTime = cut.EventTime,
                EventType = cut.EventType,
                Cut = cut.Cut,
            };
        }

        /// <summary>同じ手で同じ時刻に2つ以上あるか。</summary>
        public static bool HasStacks(IEnumerable<ReplayNote> notes)
        {
            var list = notes.Where(n => n.EventType != NoteEventType.Bomb).ToList();
            if (list.Count < 2) return false;

            // ノーツの時刻が入っていない（全部同じ）記録は見分けられないので、まとめない。
            if (list.Max(n => n.SpawnTime) - list.Min(n => n.SpawnTime) < SameTime) return false;

            var seen = new HashSet<long>();
            foreach (var n in list)
            {
                long key = (long)Math.Round(n.SpawnTime / SameTime) * 2 + n.ColorType;
                if (!seen.Add(key)) return true;
            }
            return false;
        }

        /// <summary>
        /// 同じ手の、続くまとまりどうしの間隔の上位 1/4 の値 (秒)。測れなければ無限大（やり直しと見なさない）。
        /// ブロックの切れ目はブロックの数しか無いので、ここには入ってこない。
        /// </summary>
        private static float TypicalGap(List<List<ReplayNote>> groups)
        {
            var gaps = new List<float>();
            for (int i = 1; i < groups.Count; i++)
                gaps.Add(groups[i][0].SpawnTime - groups[i - 1][0].SpawnTime);
            if (gaps.Count == 0) return float.PositiveInfinity;

            gaps.Sort();
            return gaps[Math.Min(gaps.Count - 1, gaps.Count * 3 / 4)];
        }

        private static IEnumerable<List<ReplayNote>> Groups(List<ReplayNote> sorted)
        {
            var group = new List<ReplayNote>();
            foreach (var n in sorted)
            {
                if (group.Count > 0 && n.SpawnTime - group[0].SpawnTime > SameTime)
                {
                    yield return group;
                    group = new List<ReplayNote>();
                }
                group.Add(n);
            }
            if (group.Count > 0) yield return group;
        }

        /// <summary>
        /// 振りの行き先。並んだノーツの両端のうち、
        /// <list type="number">
        /// <item>矢印が両端を結ぶ線に沿っていれば、矢印の向きに進んだ側（axis はこれで決まる）</item>
        /// <item>そうでなければ（ドット・垂直・s で倒した矢印）、前の行き先から遠い側</item>
        /// <item>最初の1本（とブロックの頭）は、譜面名の遷移で最初に叩く点</item>
        /// </list>
        /// 矢印が線に沿っていないときに矢印で決めると、垂直を上下左右に倒した斜めのタワーで
        /// 始点の側を選んでしまう。
        /// </summary>
        private static ReplayNote Destination(List<ReplayNote> group, GridPosition? previous, GridPosition? hint)
        {
            ReplayNote a = group[0], b = group[0];
            int best = -1;
            for (int i = 0; i < group.Count; i++)
                for (int j = i + 1; j < group.Count; j++)
                {
                    int d = Math.Abs(group[i].LineIndex - group[j].LineIndex) + Math.Abs(group[i].LineLayer - group[j].LineLayer);
                    if (d > best) { best = d; a = group[i]; b = group[j]; }
                }

            int dx, dy;
            ((CutDirection)a.CutDirection).ToVector(out dx, out dy);
            int lx = b.LineIndex - a.LineIndex, ly = b.LineLayer - a.LineLayer;
            bool alongLine = (dx != 0 || dy != 0) && dx * ly - dy * lx == 0;
            if (alongLine)
                return dx * lx + dy * ly > 0 ? b : a;

            var posA = PositionOf(a);
            var posB = PositionOf(b);
            if (previous.HasValue)
            {
                int da = Distance(posA, previous.Value), db = Distance(posB, previous.Value);
                if (da != db) return da > db ? a : b;
            }
            if (hint.HasValue)
            {
                if (posA == hint.Value) return a;
                if (posB == hint.Value) return b;
            }
            return a;
        }

        /// <summary>譜面名の遷移から、手ごとに最初に叩く点（記法の2点目）。</summary>
        private static Dictionary<int, GridPosition?> FirstTargets(string songName)
        {
            var result = new Dictionary<int, GridPosition?>();
            foreach (var seq in DrillNaming.ParseSequences(songName))
            {
                int hand = (int)seq.Hand;
                if (!result.ContainsKey(hand)) result[hand] = seq.StepAt(1).Position;
            }
            return result;
        }

        private static int Distance(GridPosition p, GridPosition q)
        {
            return Math.Max(Math.Abs(p.LineIndex - q.LineIndex), Math.Abs(p.LineLayer - q.LineLayer));
        }

        private static GridPosition PositionOf(ReplayNote note)
        {
            int x = Math.Max(0, Math.Min(GridPosition.Columns - 1, note.LineIndex));
            int y = Math.Max(0, Math.Min(GridPosition.Rows - 1, note.LineLayer));
            return new GridPosition(x, y);
        }
    }
}
