using System;
using System.Collections.Generic;
using JumpDrill.Model;
using JumpDrill.Parsing;

namespace JumpDrillMod.UI
{
    /// <summary>
    /// グリッドを叩いて遷移を組み立てる。JumpDrill の GUI のグリッドと同じ振る舞い。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>置けるのは<b>片手2点まで</b>。測るのは2点の間をどれだけ直線で振れたかなので、
    ///         行き先と戻り先の2点があれば足りる</item>
    ///   <item>置いてある点をもう一度押すと外す</item>
    ///   <item>2点が埋まっているところへ別の点を置くと、そこから置き直す</item>
    ///   <item>矢印を上下左右に倒す（記法の <c>s</c>）のは点ごと</item>
    /// </list>
    /// 組み立て途中は1点しかないことがあり、その状態は <c>HandSequence</c>（2点以上が要る）では
    /// 表せないため、点の並びだけを持ち、記法にするところは JumpDrill.Core に任せる。
    /// </remarks>
    internal class SequenceEditor
    {
        /// <summary>
        /// グリッドの記号。JumpDrill.Core の <c>GridPosition</c> と同じ並び。
        /// <code>
        /// 1 2 3 4
        /// 5 6 7 8
        /// 9 a b c
        /// </code>
        /// </summary>
        internal static readonly char[] Tokens =
        {
            '1', '2', '3', '4',
            '5', '6', '7', '8',
            '9', 'a', 'b', 'c',
        };

        /// <summary>片手あたりに置ける点の数。GUI の <c>GridPicker.MaxSteps</c> と同じ。</summary>
        internal const int MaxSteps = 2;

        private readonly List<SequenceStep> steps = new List<SequenceStep>();

        /// <summary>遷移を組む手。両手のときは、この手を元にもう片方へ反転する。</summary>
        internal Hand Hand { get; set; } = Hand.Right;

        internal int Count => steps.Count;

        internal IReadOnlyList<SequenceStep> Steps => steps;

        /// <summary>
        /// マスを押す。置いてあれば外し、無ければ置く。
        /// 2点が埋まっていれば、そこから置き直す（古い方を押し出すと、直したかった点まで消えて分かりにくい）。
        /// </summary>
        internal void Tap(char token)
        {
            var position = GridPosition.Parse(token);

            int existing = steps.FindIndex(s => s.Position == position);
            if (existing >= 0)
            {
                steps.RemoveAt(existing);
                return;
            }

            if (steps.Count >= MaxSteps) steps.Clear();
            steps.Add(new SequenceStep(position, null, false));
        }

        /// <summary>その点の矢印を上下左右に倒すかを入り切りする。</summary>
        internal void ToggleStraighten(int index)
        {
            if (index < 0 || index >= steps.Count) return;

            var step = steps[index];
            steps[index] = new SequenceStep(step.Position, step.ExplicitDirection, !step.Straighten);
        }

        internal void Clear() => steps.Clear();

        /// <summary>JumpDrill.Core の記法。例 <c>R47</c>、<c>R4s7</c>。GUI の欄に出るものと同じ。</summary>
        internal string Notation => HandSequence.Compose(Hand, steps);

        /// <summary>
        /// 記法から読み戻す。設定ファイルを手で直したときのため。
        /// グリッドで組めない形（3点以上、複数の手）や読めないものは、何も変えずに false。
        /// </summary>
        internal bool Load(string? notation)
        {
            if (string.IsNullOrWhiteSpace(notation)) return false;

            try
            {
                var parsed = SequenceParser.ParseAll(notation!);
                if (parsed.Count != 1 || parsed[0].Steps.Count > MaxSteps) return false;

                steps.Clear();
                steps.AddRange(parsed[0].Steps);
                Hand = parsed[0].Hand;
                return true;
            }
            catch (FormatException e)
            {
                Plugin.Log?.Warn($"could not read sequence '{notation}': {e.Message}");
                return false;
            }
            catch (ArgumentException e)
            {
                // 1点だけの記法は HandSequence を作れない
                Plugin.Log?.Warn($"could not read sequence '{notation}': {e.Message}");
                return false;
            }
        }
    }
}
