using System;
using System.Globalization;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Attributes;
using IPA.Utilities.Async;
using JumpDrillMod.Services;
using TMPro;

namespace JumpDrillMod.UI
{
    /// <summary>
    /// Mods タブの Replays。リプレイの容量、ドリルごとに残す数、自動削除、今すぐ消すボタン。
    /// </summary>
    /// <remarks>
    /// 消すのは順位の上位 N 件より下の記録（JumpDrill の GUI の「リプレイの整理」と同じ）。
    /// N は 10 以上で、既定は 20。
    /// Delete now は2回押す。1回目で、いくつ・何 MB 消えるかを出す（記録を全部解析するので少し待つ）。
    /// その間に N を変えたら取り消す。
    /// </remarks>
    internal partial class JumpDrillTabController
    {
        private readonly ReplayCleanupService cleanup;

        [UIComponent("replay-usage")] private TextMeshProUGUI? replayUsage = null;
        [UIComponent("replay-note")] private TextMeshProUGUI? replayNote = null;

        /// <summary>消すものを数えている最中。</summary>
        private bool planning;

        [UIValue("replay-keep")]
        public int ReplayKeep
        {
            get => ReplayCleanupService.Keep;
            set
            {
                if (Config != null) Config.ReplayKeep = Math.Max(ReplayCleanupService.MinKeep, value);
            }
        }

        [UIValue("replay-auto")]
        public bool ReplayAuto
        {
            get => Config?.ReplayAutoDelete ?? false;
            set { if (Config != null) Config.ReplayAutoDelete = value; }
        }

        /// <summary>容量の行を出し直す。ファイルの大きさを足すだけなので、すぐ終わる。</summary>
        private void RefreshReplayUsage()
        {
            if (replayUsage == null) return;
            try
            {
                int files;
                long bytes;
                ReplayScoreReader.Usage(out files, out bytes);
                replayUsage.text = string.Format(CultureInfo.InvariantCulture,
                    "Drill replays: {0} file(s), {1}", files, ReplayCleanupService.Megabytes(bytes));
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("could not measure the replays: " + e.Message);
            }
        }

        /// <summary>
        /// 全部のドリルについて、上位より下の記録を消す。押したらすぐ消す
        /// （2回押す形は慣れないとのことでやめた、ユーザー指定）。消したものは下の文に出す。
        /// </summary>
        [UIAction("replay-delete")]
        public void DeleteReplays()
        {
            if (planning) return;

            planning = true;
            SetReplayNote("Deleting...");
            cleanup.Prepare();
            int keep = ReplayCleanupService.Keep;

            Task.Run(() => cleanup.PlanAll(keep)).ContinueWith(task =>
            {
                planning = false;
                if (task.IsFaulted)
                {
                    Plugin.Log?.Error("could not count the replays: " + task.Exception);
                    SetReplayNote("Could not read the replays. See the log.");
                    return;
                }

                var plan = task.Result;
                if (plan.Targets.Count == 0)
                {
                    SetReplayNote(string.Format(CultureInfo.InvariantCulture, "Nothing to delete. Every drill has {0} records or fewer.", keep));
                    return;
                }

                var result = ReplayCleanupService.DeleteFiles(plan.Files, plan.Targets.Count);
                SetReplayNote(string.Format(CultureInfo.InvariantCulture,
                    "Deleted {0} record(s) outside the top {1} by REPRO and ACC ({2} file(s), {3}).{4}",
                    result.Plays, keep, result.Files, ReplayCleanupService.Megabytes(result.Bytes),
                    result.Failed > 0 ? " Some files could not be deleted. See the log." : string.Empty));
                RefreshReplayUsage();
            }, UnityMainThreadTaskScheduler.Default);
        }

        private void SetReplayNote(string text)
        {
            if (replayNote != null) replayNote.text = text;
        }
    }
}
