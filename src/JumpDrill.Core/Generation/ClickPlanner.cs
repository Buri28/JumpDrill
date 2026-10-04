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
            var result = new List<ClickPlanEntry>();

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
                }
            }

            return result;
        }
    }

    public struct ClickPlanEntry
    {
        public int StepIndex { get; }
        public bool Accent { get; }

        public ClickPlanEntry(int stepIndex, bool accent)
        {
            StepIndex = stepIndex;
            Accent = accent;
        }
    }
}
