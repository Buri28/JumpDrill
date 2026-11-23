using System.Collections.Generic;
using JumpDrill.Model;

namespace JumpDrill.Generation
{
    /// <summary>どのステップでクリックを鳴らすかを決める（設計メモ §3）。</summary>
    public static class ClickPlanner
    {
        /// <summary>
        /// ステップごとの「切り下げ側か」の判定。方向が水平やドットで
        /// 上下が定義できないときは、循環の偶奇をフォア／バックの代わりに使う。
        /// </summary>
        public static bool IsDownStep(IReadOnlyList<DrillNote> notesAtStep, int stepIndex)
        {
            bool sawDown = false, sawUp = false;
            for (int i = 0; i < notesAtStep.Count; i++)
            {
                var d = notesAtStep[i].Direction;
                if (d.IsDownward()) sawDown = true;
                else if (d.IsUpward()) sawUp = true;
            }

            if (sawDown) return true;
            if (sawUp) return false;
            return stepIndex % 2 == 0;
        }

        /// <summary>
        /// ステップ番号 → (鳴らすか, アクセントか)。
        /// notesByStep はステップ番号順に並んでいること。
        /// </summary>
        public static List<ClickPlanEntry> Plan(IReadOnlyList<IReadOnlyList<DrillNote>> notesByStep, ClickMode mode)
        {
            return Plan(notesByStep, mode, 1, BeatsPerBar, notesByStep.Count);
        }

        /// <summary>小節の長さ（拍）。粒度 Beat の強拍の間隔。</summary>
        public const int BeatsPerBar = 4;

        /// <param name="stepsPerBeat">1拍あたりのステップ数（粒度 Beat だけが使う）。</param>
        /// <param name="stepsPerBar">1小節のステップ数（粒度 Beat だけが使う）。</param>
        /// <param name="stepsPerBlock">1ブロックのステップ数。粒度 Beat は拍をブロックの頭から数える。</param>
        public static List<ClickPlanEntry> Plan(IReadOnlyList<IReadOnlyList<DrillNote>> notesByStep, ClickMode mode,
            int stepsPerBeat, int stepsPerBar, int stepsPerBlock)
        {
            var result = new List<ClickPlanEntry>();
            if (stepsPerBeat < 1) stepsPerBeat = 1;
            if (stepsPerBar < 1) stepsPerBar = 1;
            if (stepsPerBlock < 1) stepsPerBlock = 1;

            for (int step = 0; step < notesByStep.Count; step++)
            {
                bool down = IsDownStep(notesByStep[step], step);

                switch (mode)
                {
                    case ClickMode.None:
                        break;

                    case ClickMode.All:
                        // 全ノーツを弱く、切り下げにアクセント。
                        result.Add(new ClickPlanEntry(step, down));
                        break;

                    case ClickMode.Down:
                        if (down) result.Add(new ClickPlanEntry(step, false));
                        break;

                    case ClickMode.Up:
                        if (!down) result.Add(new ClickPlanEntry(step, false));
                        break;

                    case ClickMode.Every4:
                        if (step % 4 == 0) result.Add(new ClickPlanEntry(step, false));
                        break;

                    case ClickMode.Beat:
                        {
                            // 小節の頭は強拍、拍の頭はふつう、拍の間は弱く
                            int inBlock = step % stepsPerBlock;
                            bool bar = inBlock % stepsPerBar == 0;
                            bool beat = inBlock % stepsPerBeat == 0;
                            result.Add(new ClickPlanEntry(step, bar, !beat));
                            break;
                        }
                }
            }

            return result;
        }
    }

    public struct ClickPlanEntry
    {
        public int StepIndex { get; }
        public bool Accent { get; }

        /// <summary>弱く鳴らすか（粒度 Beat の拍の間）。</summary>
        public bool Weak { get; }

        public ClickPlanEntry(int stepIndex, bool accent)
            : this(stepIndex, accent, false) { }

        public ClickPlanEntry(int stepIndex, bool accent, bool weak)
        {
            StepIndex = stepIndex;
            Accent = accent;
            Weak = weak && !accent;
        }
    }
}
