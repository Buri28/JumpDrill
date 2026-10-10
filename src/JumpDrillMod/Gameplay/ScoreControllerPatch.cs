using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace JumpDrillMod.Gameplay
{
    /// <summary>
    /// 採点が各ノーツを扱う直前に、記録へノーツの時刻を入れる。
    /// </summary>
    /// <remarks>
    /// BeatLeader はノーツの時刻を、切った瞬間ではなく本体の採点（<c>ScoreController.LateUpdate</c>）が
    /// そのノーツを取り上げたときの曲の時刻で残す。採点は時刻の順に進み、まだ先のノーツ
    /// （曲の時刻 + 0.15 秒より後で、それより前に採点を待つノーツがあるもの）は後回しにするので、
    /// 早く切ったノーツほど切った瞬間より後の時刻になる。JumpDrill.Core の解析は
    /// この時刻で振りの区間を切り出すので、同じ数字を出すには同じところで刻む必要がある。
    /// BeatLeader の <c>ReplayRecorder.OnBeforeScoreControllerLateUpdate</c> と同じ。
    /// </remarks>
    [HarmonyPatch(typeof(ScoreController), "LateUpdate")]
    internal static class ScoreControllerPatch
    {
        /// <summary>
        /// 外されていたらかけ直す。記録を始めるたびに呼ぶ。
        /// </summary>
        /// <remarks>
        /// BeatLeader はリプレイの再生中この処理を止めていて、再生を終えると
        /// 自分の分だけでなく<b>全MODの Prefix</b> を外す（<c>HarmonySilencer.Dispose</c> が
        /// <c>Unpatch(method, Prefix, "*")</c>）。そのままだと再生の後はノーツが1つも記録されない。
        /// </remarks>
        internal static void EnsurePatched()
        {
            try
            {
                var original = AccessTools.Method(typeof(ScoreController), "LateUpdate");
                if (original == null) return;

                var info = Harmony.GetPatchInfo(original);
                if (info != null && info.Prefixes.Any(p => p.owner == Plugin.HarmonyId)) return;

                new Harmony(Plugin.HarmonyId).CreateClassProcessor(typeof(ScoreControllerPatch)).Patch();
                Plugin.Log?.Info("ScoreController patch was removed by another mod; patched again");
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not patch ScoreController again: " + e);
            }
        }

        private static void Prefix(AudioTimeSyncController ____audioTimeSyncController,
            List<float> ____sortedNoteTimesWithoutScoringElements,
            List<ScoringElement> ____sortedScoringElementsWithoutMultiplier)
        {
            DrillReplayRecorder.Active?.OnBeforeScoring(
                ____audioTimeSyncController,
                ____sortedNoteTimesWithoutScoringElements,
                ____sortedScoringElementsWithoutMultiplier);
        }
    }
}
