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

        /// <summary>1回目の Delete now で決めた、消すもの。2回目で消す。</summary>
        private ReplayCleanupService.Plan? armed;

        /// <summary>消すものを数えている最中。</summary>
        private bool planning;

        [UIValue("replay-keep")]
        public int ReplayKeep
        {
            get => ReplayCleanupService.Keep;
            set
            {
                if (Config != null) Config.ReplayKeep = Math.Max(ReplayCleanupService.MinKeep, value);
                Disarm();
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

        [UIAction("replay-delete")]
        public void DeleteReplays()
        {
            if (planning) return;

            if (armed == null)
            {
                planning = true;
                SetReplayNote("Counting the records...");
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

                    armed = plan;
                    SetReplayNote(string.Format(CultureInfo.InvariantCulture,
                        "<color=#FF8060>Press Delete now again to delete {0} record(s) below the top {1} ({2} file(s), {3}). It cannot be undone.</color>",
                        plan.Targets.Count, keep, plan.Files.Count, ReplayCleanupService.Megabytes(plan.Bytes)));
                }, UnityMainThreadTaskScheduler.Default);
                return;
            }

            var target = armed;
            armed = null;
            var result = ReplayCleanupService.DeleteFiles(target.Files, target.Targets.Count);
            SetReplayNote(string.Format(CultureInfo.InvariantCulture,
                "Deleted {0} record(s) ({1} file(s), {2}).{3}",
                result.Plays, result.Files, ReplayCleanupService.Megabytes(result.Bytes),
                result.Failed > 0 ? " Some files could not be deleted. See the log." : string.Empty));
            RefreshReplayUsage();
        }

        private void Disarm()
        {
            if (armed == null) return;
            armed = null;
            SetReplayNote(string.Empty);
        }

        private void SetReplayNote(string text)
        {
            if (replayNote != null) replayNote.text = text;
        }
    }
}
