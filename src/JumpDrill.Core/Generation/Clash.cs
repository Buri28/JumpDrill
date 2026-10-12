using System;
using System.Collections.Generic;
using JumpDrill.Model;

namespace JumpDrill.Generation
{
    /// <summary>左右のセイバーの通り道が重なる配置（クラッシュ配置）で、譜面にできない。</summary>
    public sealed class ClashException : InvalidOperationException
    {
        public ClashException()
            : base(Lang.T(
                "左右の手の通り道が重なるので、手がぶつかります（クラッシュ配置）。遷移か配置を変えてください。",
                "The paths of both hands overlap and the hands would hit each other. Change the layout or the hand pattern."))
        {
        }
    }

    /// <summary>
    /// クラッシュ配置の判定。左右の手の遷移が<b>同じ直線の上で重なる</b>と、
    /// 続けて（または同時に）振る左右の手がぶつかる。<c>Rca, L9b</c> は下の段を両手が行き来する。
    ///
    /// 交互（alt・alt-trip）では、斜めに交わるだけ（<c>R1b, L49</c> の X）は向きが違うのでぶつからない。
    /// 端の1点で触れるだけのものも止めない。
    /// 同時（sync）は左右が同じ時刻に動くので、交わる・触れるだけでもぶつかる。線が少しでも重なれば止める。
    /// 片手ずつ（split）は手を替えるまで間が空くので見ない。
    /// </summary>
    public static class Clash
    {
        /// <exception cref="ClashException">左右の遷移が同じ直線の上で重なる。</exception>
        public static void Check(DrillOptions options)
        {
            if (options.HandPattern == HandPattern.Split) return;

            bool sync = options.HandPattern == HandPattern.Sync;
            var sequences = options.Sequences;
            for (int a = 0; a < sequences.Count; a++)
                for (int b = a + 1; b < sequences.Count; b++)
                {
                    if (sequences[a].Hand == sequences[b].Hand) continue;

                    foreach (var p in Segments(sequences[a]))
                        foreach (var q in Segments(sequences[b]))
                            if (sync ? Intersect(p[0], p[1], q[0], q[1]) : OverlapOnSameLine(p[0], p[1], q[0], q[1]))
                                throw new ClashException();
                }
        }

        private static IEnumerable<GridPosition[]> Segments(HandSequence sequence)
        {
            for (int i = 0; i < sequence.Length; i++)
                yield return new[] { sequence.StepAt(i).Position, sequence.StepAt(i + 1).Position };
        }

        /// <summary>2本の線分が同じ直線に乗り、長さのある区間を共有するか。</summary>
        internal static bool OverlapOnSameLine(GridPosition p0, GridPosition p1, GridPosition q0, GridPosition q1)
        {
            int dx = p1.LineIndex - p0.LineIndex, dy = p1.LineLayer - p0.LineLayer;
            if (dx == 0 && dy == 0) return false;

            // q の両端が p の直線上にあるか（外積が 0）。
            if (Cross(dx, dy, q0.LineIndex - p0.LineIndex, q0.LineLayer - p0.LineLayer) != 0) return false;
            if (Cross(dx, dy, q1.LineIndex - p0.LineIndex, q1.LineLayer - p0.LineLayer) != 0) return false;

            // 直線上の位置（p0 からの内積）で区間を比べる。
            int pEnd = dx * dx + dy * dy;
            int qa = Dot(dx, dy, q0.LineIndex - p0.LineIndex, q0.LineLayer - p0.LineLayer);
            int qb = Dot(dx, dy, q1.LineIndex - p0.LineIndex, q1.LineLayer - p0.LineLayer);

            int lo = Math.Max(0, Math.Min(qa, qb));
            int hi = Math.Min(pEnd, Math.Max(qa, qb));
            return hi > lo;
        }

        /// <summary>2本の線分が1点でも共有するか（交わる・端で触れる・重なる）。</summary>
        internal static bool Intersect(GridPosition p0, GridPosition p1, GridPosition q0, GridPosition q1)
        {
            int d1 = Orientation(q0, q1, p0), d2 = Orientation(q0, q1, p1);
            int d3 = Orientation(p0, p1, q0), d4 = Orientation(p0, p1, q1);

            if (d1 * d2 < 0 && d3 * d4 < 0) return true;
            return (d1 == 0 && OnSegment(q0, q1, p0)) || (d2 == 0 && OnSegment(q0, q1, p1))
                || (d3 == 0 && OnSegment(p0, p1, q0)) || (d4 == 0 && OnSegment(p0, p1, q1));
        }

        private static int Orientation(GridPosition a, GridPosition b, GridPosition c)
        {
            return Math.Sign(Cross(b.LineIndex - a.LineIndex, b.LineLayer - a.LineLayer,
                                   c.LineIndex - a.LineIndex, c.LineLayer - a.LineLayer));
        }

        /// <summary>a-b の直線上にある c が、線分 a-b の範囲に入っているか。</summary>
        private static bool OnSegment(GridPosition a, GridPosition b, GridPosition c)
        {
            return Math.Min(a.LineIndex, b.LineIndex) <= c.LineIndex && c.LineIndex <= Math.Max(a.LineIndex, b.LineIndex)
                && Math.Min(a.LineLayer, b.LineLayer) <= c.LineLayer && c.LineLayer <= Math.Max(a.LineLayer, b.LineLayer);
        }

        private static int Cross(int ax, int ay, int bx, int by) { return ax * by - ay * bx; }

        private static int Dot(int ax, int ay, int bx, int by) { return ax * bx + ay * by; }
    }
}
