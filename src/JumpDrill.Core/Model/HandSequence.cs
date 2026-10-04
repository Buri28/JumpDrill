using System;
using System.Collections.Generic;
using System.Linq;

namespace JumpDrill.Model
{
    /// <summary>片手ぶんの循環。2点でも3点以上でもよい（設計メモ §2）。</summary>
    public sealed class HandSequence
    {
        public Hand Hand { get; }

        /// <summary>巡回する点列。末尾から先頭へ戻る。</summary>
        public IReadOnlyList<SequenceStep> Steps { get; }

        public HandSequence(Hand hand, IReadOnlyList<SequenceStep> steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            if (steps.Count < 2)
                throw new ArgumentException(Lang.T("遷移には最低2点が要ります。", "A sequence needs at least 2 points."), nameof(steps));

            Hand = hand;
            Steps = steps;
        }

        public int Length { get { return Steps.Count; } }

        public SequenceStep StepAt(int index)
        {
            return Steps[((index % Length) + Length) % Length];
        }

        public override string ToString()
        {
            return Compose(Hand, Steps);
        }

        /// <summary>
        /// 記法に書き戻す。<c>R4sbs</c> の短い形。
        ///
        /// <c>@</c> で向きを明示した点があるときだけ <c>R:4@ul&gt;b</c> の形にする。
        /// 短い形では <c>4@ulb</c> の向きが "ul" なのか "ulb" なのか切り出せないため。
        /// </summary>
        public static string Compose(Hand hand, IReadOnlyList<SequenceStep> steps)
        {
            string prefix = hand == Hand.Right ? "R" : "L";
            if (steps == null || steps.Count == 0) return prefix;

            var parts = steps.Select(s => s.ToString()).ToArray();

            foreach (var step in steps)
                if (step.ExplicitDirection.HasValue)
                    return prefix + ":" + string.Join(">", parts);

            return prefix + string.Concat(parts);
        }
    }
}
