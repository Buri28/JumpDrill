using System;
using System.Collections.Generic;
using JumpDrill.Model;

namespace JumpDrill.Generation
{
    /// <summary>タワーで両手のノーツが同じマスに重なり、譜面にできない。</summary>
    public sealed class TowerOverlapException : InvalidOperationException
    {
        public TowerOverlapException()
            : base(Lang.T(
                "タワーにすると両手のノーツが同じマスに重なるので作れません。遷移を変えるか、配置パターンを片手ずつにしてください。",
                "With tower, notes of both hands land on the same cell. Change the layout, or use split."))
        {
        }
    }

    /// <summary>
    /// タワー（とスタック）。1つ前の点から今の点までが縦・横・45°斜めの一直線なら、
    /// 間のマスも同じ時刻・同じ向きのノーツで埋める。
    ///
    /// <code>
    ///   3&gt;b   b を叩く時刻に 3 7 b すべて ▼、戻りは 3 7 b すべて ▲（縦3つのタワー）
    ///   1&gt;4   1 2 3 4 すべて ▶ / ◀                         （横4つ）
    ///   3&gt;7   3 7 の2つ                                     （スタック）
    /// </code>
    ///
    /// 一直線にならない遷移（4&gt;b のような桂馬跳び）はそのまま1ノーツ。
    /// </summary>
    public static class Tower
    {
        /// <summary>from から to までのマス。両端を含む。一直線でなければ null。</summary>
        public static List<GridPosition> Cells(GridPosition from, GridPosition to)
        {
            int dx = to.LineIndex - from.LineIndex;
            int dy = to.LineLayer - from.LineLayer;
            if (dx == 0 && dy == 0) return null;
            if (dx != 0 && dy != 0 && System.Math.Abs(dx) != System.Math.Abs(dy)) return null;

            int n = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
            int sx = System.Math.Sign(dx), sy = System.Math.Sign(dy);

            var cells = new List<GridPosition>(n + 1);
            for (int i = 0; i <= n; i++)
                cells.Add(new GridPosition(from.LineIndex + sx * i, from.LineLayer + sy * i));
            return cells;
        }

        /// <summary>
        /// タワーにしたときにその手のノーツが置かれるマスすべて（循環の点と、点の間を埋めるマス）。
        /// カバー画像やアイコンで、ノーツのある所に印を付けるのに使う。
        /// </summary>
        public static List<GridPosition> NoteCells(HandSequence sequence)
        {
            var result = new List<GridPosition>();
            foreach (var step in sequence.Steps)
                if (!result.Contains(step.Position)) result.Add(step.Position);

            for (int i = 0; i < sequence.Length; i++)
            {
                var cells = Cells(sequence.StepAt(i).Position, sequence.StepAt(i + 1).Position);
                if (cells == null) continue;
                foreach (var cell in cells)
                    if (!result.Contains(cell)) result.Add(cell);
            }
            return result;
        }

        /// <summary>
        /// 交互（1ステップごと・往復ごと）で、両手のタワーが重なる配置を作らせない。
        /// 同時なら同じマスに重なる配置は、交互では続けて叩く左右のセイバーがぶつかる。
        /// 判定は同時と同じく「同じ回数目の振りどうし」が同じマスを使うか。
        /// 同時は生成しながら <see cref="Merge"/> で見るので、ここでは見ない。
        /// </summary>
        /// <exception cref="TowerOverlapException">両手のノーツが同じマスに重なる。</exception>
        internal static void CheckCrossing(DrillOptions options)
        {
            if (options.HandPattern != HandPattern.Alternate && options.HandPattern != HandPattern.AlternateRoundTrip) return;

            var sequences = options.Sequences;
            for (int a = 0; a < sequences.Count; a++)
                for (int b = a + 1; b < sequences.Count; b++)
                {
                    int laps = sequences[a].Length * sequences[b].Length;
                    for (int i = 1; i <= laps; i++)
                    {
                        // 両手ともタワーにならない振り（一直線でない）なら比べない。タワーが無いのに止めてしまう。
                        // 片手だけタワーなら、もう片方は着地点の1マスで比べる（タワーの間のマスに降りるとぶつかる）。
                        var cellsA = TowerCells(sequences[a], i);
                        var cellsB = TowerCells(sequences[b], i);
                        if (cellsA == null && cellsB == null) continue;
                        cellsA = cellsA ?? new List<GridPosition> { sequences[a].StepAt(i).Position };
                        cellsB = cellsB ?? new List<GridPosition> { sequences[b].StepAt(i).Position };
                        foreach (var cell in cellsA)
                            if (cellsB.Contains(cell)) throw new TowerOverlapException();
                    }
                }
        }

        /// <summary>i 回目の振りのタワーのマス。一直線でなければ（タワーにならなければ）null。</summary>
        private static List<GridPosition> TowerCells(HandSequence sequence, int index)
        {
            return Cells(sequence.StepAt(index - 1).Position, sequence.StepAt(index).Position);
        }

        /// <summary>
        /// 間を埋めるノーツを足す。
        /// 両手で同じマスを取り合うときは作れない（どちらかを落とすと左右で形が変わる）。
        /// ここで起きるのは両手同時（sync）だけ。交互は <see cref="CheckCrossing"/> で先に止める。
        /// </summary>
        /// <exception cref="TowerOverlapException">両手のノーツが同じマスに重なる。</exception>
        internal static void Merge(List<DrillNote> atStep, List<DrillNote> extra)
        {
            var taken = new HashSet<GridPosition>();
            foreach (var note in atStep) taken.Add(note.Position);

            foreach (var note in extra)
            {
                if (!taken.Add(note.Position)) throw new TowerOverlapException();
                atStep.Add(note);
            }
        }
    }
}
