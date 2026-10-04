using System;
using System.Collections.Generic;
using System.Linq;

namespace JumpDrill.Replays
{
    /// <summary>
    /// ノーツの並ぶ格子。本体の <c>StaticBeatmapObjectSpawnMovementData</c> と同じ。
    ///
    /// 横は <c>Get2DNoteOffset</c>: <c>(lineIndex - (本数-1)/2) * 0.6</c>。
    ///
    /// 縦は <c>LineYPosForLineLayer</c>（0.25 / 0.85 / 1.45）<b>ではない</b>。
    /// ノーツは飛んでくる間に放物線を描き、その式
    /// （<c>JumpPosYForLineLayerAtDistanceFromPlayer</c>）を整理すると
    /// <c>y = LineYPos + (Highest - LineYPos) * (2τ - τ²)</c>、
    /// <c>τ = 1 - 距離 / (飛距離/2)</c> になる。
    /// τ=0（飛距離の半分の地点）で <c>LineYPos</c>、τ=1（プレイヤー位置）で
    /// <c>HighestJumpPosY</c>。<b>叩くのは後者</b>なので、そちらを使う。
    ///
    /// 実測でも、<c>LineYPos</c> だと残差 0.155 m、<c>HighestJumpPosY</c> なら 0.083 m。
    ///
    /// さらに実際の高さには身長ぶんの下駄が乗る（<c>jumpOffsetY</c>）。
    /// その値はリプレイに入っていないので、切った位置から当てはめる（<see cref="FitTo"/>）。
    /// </summary>
    public sealed class NoteGrid
    {
        /// <summary>隣の列との間隔 (m)。</summary>
        public const float LineSpacing = 0.6f;

        /// <summary>標準の列数。</summary>
        public const int LineCount = 4;

        public NoteGrid(float offsetX, float offsetY)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public float OffsetX { get; }
        public float OffsetY { get; }

        public float X(int lineIndex)
        {
            return (lineIndex - (LineCount - 1) / 2f) * LineSpacing + OffsetX;
        }

        public float Y(int lineLayer)
        {
            return BaseY(lineLayer) + OffsetY;
        }

        /// <summary>
        /// 身長ぶんの下駄を除いた、<b>プレイヤー位置に届いたときの</b>高さ。
        /// 本体の <c>_baseLinesHighestJumpPosY</c> / <c>_upperLines...</c> / <c>_topLines...</c>。
        ///
        /// 段の間隔が一定でないことに注意（0.55 と 0.50）。
        /// <c>LineYPosForLineLayer</c> の 0.6 刻みは飛距離の半分の地点での値で、
        /// 叩く位置ではない。
        /// </summary>
        public static float BaseY(int lineLayer)
        {
            if (lineLayer <= 0) return 0.85f;
            if (lineLayer == 1) return 1.40f;
            return 1.90f;
        }

        /// <summary>飛距離の半分の地点での高さ（<c>LineYPosForLineLayer</c>）。</summary>
        public static float MidJumpY(int lineLayer)
        {
            if (lineLayer <= 0) return 0.25f;
            if (lineLayer == 1) return 0.85f;
            return 1.45f;
        }

        /// <summary>
        /// 切った位置に格子を当てはめる。ずらす量だけを最小二乗で決め、
        /// 間隔は本体の値のまま使う。
        ///
        /// 切った点はノーツの中心そのものではなく、振り込む側に寄る。
        /// そのぶん残差が出るので、<paramref name="residual"/> で返す。
        /// </summary>
        public static NoteGrid FitTo(Replay replay, out double residual)
        {
            residual = 0.0;
            if (replay == null) return new NoteGrid(0f, 0f);

            var cuts = replay.Notes
                .Where(n => n.EventType == NoteEventType.Good && n.Cut != null)
                .ToList();

            if (cuts.Count == 0) return new NoteGrid(0f, 0f);

            double dx = cuts.Average(n => n.Cut.CutPoint.X - (n.LineIndex - (LineCount - 1) / 2f) * LineSpacing);
            double dy = cuts.Average(n => n.Cut.CutPoint.Y - BaseY(n.LineLayer));

            var grid = new NoteGrid((float)dx, (float)dy);

            double squared = 0.0;
            foreach (var note in cuts)
            {
                double ex = note.Cut.CutPoint.X - grid.X(note.LineIndex);
                double ey = note.Cut.CutPoint.Y - grid.Y(note.LineLayer);
                squared += ex * ex + ey * ey;
            }
            residual = Math.Sqrt(squared / cuts.Count);

            return grid;
        }

        /// <summary>
        /// 実際に切った位置の平均。配置ごと。
        ///
        /// 格子は本体どおりでも、切る所はノーツの中心から振り込む側に寄る。
        /// どちらで見たいかは人によるので、両方出せるようにしておく。
        /// </summary>
        public static Dictionary<NotePlacement, Vector3> CutCenters(Replay replay)
        {
            var result = new Dictionary<NotePlacement, Vector3>();
            if (replay == null) return result;

            var groups = replay.Notes
                .Where(n => n.EventType == NoteEventType.Good && n.Cut != null)
                .GroupBy(n => new NotePlacement
                {
                    ColorType = n.ColorType,
                    LineIndex = n.LineIndex,
                    LineLayer = n.LineLayer,
                    CutDirection = n.CutDirection,
                });

            foreach (var group in groups)
            {
                result[group.Key] = new Vector3
                {
                    X = group.Average(n => n.Cut.CutPoint.X),
                    Y = group.Average(n => n.Cut.CutPoint.Y),
                    Z = group.Average(n => n.Cut.CutPoint.Z),
                };
            }

            return result;
        }

        /// <summary>この譜面で使われている配置。</summary>
        public static List<NotePlacement> PlacementsOf(Replay replay)
        {
            var result = new List<NotePlacement>();
            if (replay == null) return result;

            foreach (var note in replay.Notes)
            {
                if (note.EventType != NoteEventType.Good && note.EventType != NoteEventType.Miss) continue;

                var placement = new NotePlacement
                {
                    ColorType = note.ColorType,
                    LineIndex = note.LineIndex,
                    LineLayer = note.LineLayer,
                    CutDirection = note.CutDirection,
                };

                if (!result.Any(p => p.Equals(placement))) result.Add(placement);
            }

            return result;
        }
    }

    /// <summary>1つのノーツの置き場所と向き。</summary>
    public struct NotePlacement : IEquatable<NotePlacement>
    {
        public int ColorType;
        public int LineIndex;
        public int LineLayer;
        public int CutDirection;

        public bool Equals(NotePlacement other)
        {
            return ColorType == other.ColorType && LineIndex == other.LineIndex &&
                   LineLayer == other.LineLayer && CutDirection == other.CutDirection;
        }

        public override bool Equals(object obj)
        {
            return obj is NotePlacement && Equals((NotePlacement)obj);
        }

        public override int GetHashCode()
        {
            return ((ColorType * 10 + LineIndex) * 10 + LineLayer) * 10 + CutDirection;
        }
    }
}
