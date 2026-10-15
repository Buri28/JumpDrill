using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Replays;
using JumpDrillMod.Models;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 順位・ベスト・メダルを REPRO（再現精度 %）で決めるか、ACC（精度 %、ゲームの Acc）で決めるか。
    /// Jump Drill 画面とリーダーボードで同じものを使う（GUI の「基準」と同じ）。
    /// </summary>
    internal static class RankSetting
    {
        /// <summary>切り替わった。出している画面は並べ直す。</summary>
        internal static event Action? Changed;

        internal static RankMetric Current =>
            PluginConfig.Instance?.RankByAccuracy == true ? RankMetric.Accuracy : RankMetric.Reproducibility;

        internal static void Set(RankMetric metric)
        {
            if (metric == Current) return;
            if (PluginConfig.Instance != null) PluginConfig.Instance.RankByAccuracy = metric == RankMetric.Accuracy;

            // 受け手ごとに呼ぶ。1つが例外を出しても（消えた画面など）、ほかの画面は切り替わるように
            var handlers = Changed;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception e) { Plugin.Log?.Error("could not switch the rank: " + e); }
            }
        }

        /// <summary>見出しに使う名前。リーダーボードの列名と同じ。</summary>
        internal static string Label(RankMetric metric) => metric == RankMetric.Accuracy ? "ACC" : "REPRO";

        internal static double ValueOf(DrillScore score, RankMetric metric) =>
            metric == RankMetric.Accuracy ? score.Accuracy : score.ReproducibilityPercent;

        /// <summary>
        /// 順位の並び。記録として数えるものを先に、その中で数字の高い順、同じなら新しい順。
        /// GUI のスコア画面の「順位」と同じ決め方。
        /// </summary>
        internal static List<DrillScore> Rank(IEnumerable<DrillScore> scores, RankMetric metric) =>
            scores.OrderBy(s => !s.CountsAsRecord)
                .ThenByDescending(s => ValueOf(s, metric))
                .ThenByDescending(s => s.PlayedAt)
                .ToList();
    }
}
