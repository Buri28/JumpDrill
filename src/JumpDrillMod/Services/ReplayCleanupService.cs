using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Replays;
using JumpDrillMod.Models;
using Zenject;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// ドリルのリプレイを消す。1件ずつの削除、上位 N 件より下の一括削除、叩いたあとの自動削除。
    /// </summary>
    /// <remarks>
    /// JumpDrill の GUI のスコア画面と同じ決まり。
    /// <list type="bullet">
    ///   <item>ごみ箱は通さず、すぐに消す（元に戻せない）。押すのは2回（1回目で何を消すかを出す）</item>
    ///   <item>同じプレイが BeatLeader・LocalLeaderboard・このMODの置き場にあれば全部消す
    ///         （1つでも残ると、読み直したときに一覧へ戻ってくる。JumpDrill.Core の <c>ReplayLibrary.SamePlayGroups</c>）</item>
    ///   <item>残すのは順位（リーダーボードの # と同じ並び）の上位 N 件。N は 10 以上</item>
    /// </list>
    /// 自動削除は、ドリルを叩いてリプレイを書き終えたときに、そのドリルだけを整理する。
    /// <b>叩いたばかりの記録は、順位が下でも次に叩くまで残す。</b>終えてすぐに一覧から消えると、
    /// いまのプレイを見返せない。
    /// </remarks>
    internal class ReplayCleanupService : IInitializable, IDisposable
    {
        internal const int MinKeep = 10;

        private readonly ReplayScoreReader reader;

        /// <summary>リプレイを消した。一覧を出している画面は読み直す。</summary>
        internal static event Action? Changed;

        internal ReplayCleanupService(ReplayScoreReader reader)
        {
            this.reader = reader;
        }

        /// <summary>ドリルごとに残す数。設定の値を 10 以上に丸める。</summary>
        internal static int Keep => Math.Max(MinKeep, PluginConfig.Instance?.ReplayKeep ?? 20);

        private static string PackName => PluginConfig.Instance?.PackName ?? "JumpDrill";

        public void Initialize()
        {
            Gameplay.DrillReplayRecorder.Saved += OnSaved;
        }

        public void Dispose()
        {
            Gameplay.DrillReplayRecorder.Saved -= OnSaved;
        }

        /// <summary>叩いたドリルを整理する（自動削除が有効なときだけ）。メインスレッドで呼ばれる。</summary>
        private void OnSaved(string path)
        {
            if (PluginConfig.Instance?.ReplayAutoDelete != true) return;

            try
            {
                string? id = IdFromFileName(path);
                if (id == null) return;

                var ranked = reader.For(id, PackName);
                var targets = Below(ranked, Keep, path);
                if (targets.Count == 0) return;

                var result = Delete(targets);
                Plugin.Log?.Info($"auto delete: {result.Plays} record(s) of {id} ({result.Files} file(s), {result.Bytes / 1024.0 / 1024.0:0.0} MB)");
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not delete old replays: " + e);
            }
        }

        /// <summary>ファイル名の中の <c>[ID]</c> を拾う。</summary>
        private static string? IdFromFileName(string path)
        {
            string name = Path.GetFileName(path);
            int open = name.IndexOf('[');
            while (open >= 0 && open + DrillNaming.IdLength + 1 < name.Length)
            {
                string? id = DrillNaming.ExtractId(name.Substring(open));
                if (id != null) return id;
                open = name.IndexOf('[', open + 1);
            }
            return null;
        }

        /// <summary>
        /// 順位の上位 <paramref name="keep"/> 件より下の記録。<paramref name="protect"/> と同じプレイは残す。
        /// </summary>
        internal static List<DrillScore> Below(IReadOnlyList<DrillScore> ranked, int keep, string? protect = null)
        {
            return ranked.Skip(keep)
                .Where(s => protect == null ||
                            (!string.Equals(s.ReplayPath, protect, StringComparison.OrdinalIgnoreCase) &&
                             ReplayLibrary.SamePlayGap(s.ReplayPath, protect) < 0))
                .ToList();
        }

        /// <summary>消すときの内訳。</summary>
        internal struct Result
        {
            public int Plays;
            public int Files;
            public long Bytes;
            public int Failed;
        }

        /// <summary>
        /// 全部のドリルについて、上位 N 件より下の記録を出す（まだ消さない）。
        /// 別スレッドから呼んでよい。先にメインスレッドで <see cref="Prepare"/> を呼ぶこと。
        /// </summary>
        internal Plan PlanAll(int keep)
        {
            var targets = reader.AllByDrill().Values.SelectMany(list => Below(list, keep)).ToList();
            var files = ReplayLibrary.SamePlayFiles(targets.Select(t => t.ReplayPath));
            return new Plan { Targets = targets, Files = files, Bytes = files.Sum(SizeOf) };
        }

        /// <summary>消す予定。</summary>
        internal sealed class Plan
        {
            public List<DrillScore> Targets = new List<DrillScore>();
            public List<string> Files = new List<string>();
            public long Bytes;
        }

        /// <summary>譜面のフォルダの控えを作り直す。メインスレッドで。</summary>
        internal void Prepare() => reader.RefreshLevelFolders(PackName);

        /// <summary>記録を消す。同じプレイのコピーも消す。</summary>
        internal static Result Delete(IEnumerable<DrillScore> scores)
        {
            var list = scores.ToList();
            return DeleteFiles(ReplayLibrary.SamePlayFiles(list.Select(s => s.ReplayPath)), list.Count);
        }

        /// <summary>決めておいたファイルを消す。終わったら <see cref="Changed"/> を出す。</summary>
        internal static Result DeleteFiles(List<string> files, int plays)
        {
            var result = new Result { Plays = plays };
            foreach (var file in files)
            {
                try
                {
                    long size = SizeOf(file);
                    if (File.Exists(file)) File.Delete(file);
                    result.Files++;
                    result.Bytes += size;
                }
                catch (IOException) { result.Failed++; }
                catch (UnauthorizedAccessException) { result.Failed++; }
            }

            Plugin.LogDebug($"deleted {result.Files} replay file(s), {result.Failed} failed");

            // 受け手は画面を書き換えるので、メインスレッドで知らせる（別スレッドで消すこともある）
            System.Threading.Tasks.Task.Factory.StartNew(() => Changed?.Invoke(), System.Threading.CancellationToken.None,
                System.Threading.Tasks.TaskCreationOptions.None, IPA.Utilities.Async.UnityMainThreadTaskScheduler.Default);
            return result;
        }

        /// <summary>1件ぶんのファイル（同じプレイのコピーを含む）と容量。</summary>
        internal static List<string> FilesOf(DrillScore score) => ReplayLibrary.SamePlayFiles(score.ReplayPath);

        internal static long SizeOf(string file)
        {
            try { return new FileInfo(file).Length; }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        internal static string Megabytes(long bytes) =>
            (bytes / 1024.0 / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
    }
}
