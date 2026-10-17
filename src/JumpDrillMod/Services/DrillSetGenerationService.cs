using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IPA.Utilities.Async;
using JumpDrill.Model;
using JumpDrill.Output;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// ドリル（14方向 × 8段 = 112本）を専用パックへ一括で書く。
    /// </summary>
    /// <remarks>
    /// 中身は JumpDrill.Core の <c>DrillSetWriter.WriteAll</c>。GUI の「ドリル一括生成」と同じものを呼ぶので、
    /// 同じ ID の譜面ができ、GUI で作ったものとも記録が混ざらずにまとまる。
    ///
    /// 譜面は MOD に同梱しない（112本の ogg を配ると重い）。初めて使うときにここで作る前提
    /// （JumpDrill.Core の <c>DrillSetWriter</c> の注釈）。
    ///
    /// <b>別スレッドで回す。</b> ogg のエンコードが1本数百 ms かかり、全部で数十秒になる。
    /// メニューで固めるとゲームが応答しなくなって見える。
    /// 途中の知らせと完了はメインスレッドに戻して呼ぶ（UI を触るのはメインスレッドだけ）。
    /// </remarks>
    internal class DrillSetGenerationService
    {
        private readonly DrillPackLocator pack;
        private readonly SongRefreshService refresher;

        internal DrillSetGenerationService(DrillPackLocator pack, SongRefreshService refresher)
        {
            this.pack = pack;
            this.refresher = refresher;
        }

        /// <summary>書いている最中なら true。二重に走らせない。</summary>
        internal bool IsRunning { get; private set; }

        /// <summary>全部で何本か。画面に出す。</summary>
        internal static int Total => DrillSet.All().Count;

        /// <summary>
        /// 全部を書き始める。<paramref name="onProgress"/> と <paramref name="onDone"/> はメインスレッドで呼ばれる。
        /// </summary>
        /// <param name="overwrite">
        /// false なら既にあるものは書き直さない。ID は設定から決まるので同じ名前なら中身も同じ。
        /// 途中でやめても、次は続きから書ける。true は作り方が変わった版で作り直すとき。
        /// </param>
        /// <param name="onProgress">1本書くごとに（書いた数, 全体）。</param>
        /// <param name="onDone">終わったら（成功したか, 画面に出す1行）。</param>
        internal void Start(bool overwrite, Action<int, int> onProgress, Action<bool, string> onDone)
        {
            Start(DrillSet.All(), overwrite, onProgress, onDone);
        }

        /// <summary>1本だけ書く。Play で無かったときと、選んだものを作り直すとき。</summary>
        internal void StartOne(DrillSetEntry entry, bool overwrite, Action<bool, string> onDone)
        {
            Start(new[] { entry }, overwrite, (done, total) => { }, onDone);
        }

        private void Start(IReadOnlyList<DrillSetEntry> entries, bool overwrite,
            Action<int, int> onProgress, Action<bool, string> onDone)
        {
            if (IsRunning) return;

            // 書き出し先はメインスレッドで決めておく。
            // 初回は folders.xml への登録が走るので、別スレッドと取り合わせたくない
            string root;
            try
            {
                root = pack.ResolveOutputFolder(PluginConfig.Instance?.PackName ?? "JumpDrill");
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not prepare the drill folder: " + e);
                onDone(false, "Could not prepare the drill folder. See the log.");
                return;
            }

            IsRunning = true;

            Task.Run(() =>
            {
                DrillSetWriter.Write(root, entries, new LevelWriteOptions { Format = AudioFormat.Ogg },
                    (done, total, name) => OnMainThread(() => onProgress(done, total)),
                    skipExisting: !overwrite);
            })
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    IsRunning = false;
                    Plugin.Log?.Error("batch generation failed: " + task.Exception);
                    onDone(false, "Generation failed. See the log.");
                    return;
                }

                // 書き終えたら曲一覧に読ませる。ここも待つ必要があるので、
                // IsRunning を落とすのは読み込みが終わってから。
                // 上書きしたときは全部読み直す。差分だけだと、読み込み済みのフォルダは見直されない
                refresher.Refresh(ok =>
                {
                    IsRunning = false;
                    onDone(ok, ok
                        ? (entries.Count == 1 ? "The drill is ready." : "The drills are ready.")
                        : "Written, but the song list did not reload. Restart the game.");
                }, fullRefresh: overwrite);
            }, UnityMainThreadTaskScheduler.Default);
        }

        /// <summary>
        /// ドリルの譜面のフォルダを消して、曲一覧を読み直させる。<paramref name="onDone"/> はメインスレッドで呼ばれる。
        /// 記録（リプレイ）は消さない。ごみ箱には送らずそのまま消す（GUI と同じ）。
        /// </summary>
        internal void Delete(IReadOnlyList<string> folders, Action<bool, string> onDone)
        {
            if (IsRunning)
            {
                onDone(false, "Generating drills. Try again when it is done.");
                return;
            }

            int failed = 0;
            foreach (var folder in folders)
            {
                try { System.IO.Directory.Delete(folder, true); }
                catch (Exception e)
                {
                    failed++;
                    Plugin.Log?.Error($"could not delete {folder}: {e.Message}");
                }
            }

            // 消した譜面は差分の読み込みでは外れないので、全部読み直す
            IsRunning = true;
            refresher.Refresh(ok =>
            {
                IsRunning = false;
                onDone(failed == 0 && ok, failed > 0
                    ? "Some maps could not be deleted. See the log."
                    : ok ? "The drill was deleted. Records are kept." : "Deleted, but the song list did not reload. Restart the game.");
            }, fullRefresh: true);
        }

        /// <summary>
        /// ドリルの譜面を同じ指定で作り直して（JumpDrill.Core の <c>DrillRebuild</c>）、曲一覧を読み直させる。
        /// 名前と ID は変わらないので記録はそのまま。<paramref name="onProgress"/> と <paramref name="onDone"/> はメインスレッドで呼ばれる。
        /// </summary>
        /// <param name="onDone">（成功したか, 画面に出す1行, 曲名を付け替えたもの 旧→新）。</param>
        internal void Rebuild(IReadOnlyList<string> folders, Action<int, int> onProgress,
            Action<bool, string, IReadOnlyDictionary<string, string>> onDone)
        {
            var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
            if (IsRunning)
            {
                onDone(false, "Generating drills. Try again when it is done.", renamed);
                return;
            }

            IsRunning = true;
            int failed = 0;

            Task.Run(() =>
            {
                for (int i = 0; i < folders.Count; i++)
                {
                    try
                    {
                        var result = DrillRebuild.Rewrite(folders[i], new LevelWriteOptions { Format = AudioFormat.Ogg });
                        if (result.Renamed) renamed[result.OldName] = result.NewName;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Plugin.Log?.Error($"could not rebuild {folders[i]}: {e}");
                    }
                    int done = i + 1;
                    OnMainThread(() => onProgress(done, folders.Count));
                }
            })
            .ContinueWith(task =>
            {
                // 書き直したフォルダは差分の読み込みでは見直されないので、全部読み直す
                refresher.Refresh(ok =>
                {
                    IsRunning = false;
                    onDone(failed == 0 && ok, failed > 0
                        ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0} of {1} map(s) could not be rebuilt. See the log.", failed, folders.Count)
                        : ok ? "Rebuilt. Records are kept." : "Rebuilt, but the song list did not reload. Restart the game.",
                        renamed);
                }, fullRefresh: true);
            }, UnityMainThreadTaskScheduler.Default);
        }

        private static void OnMainThread(Action action)
        {
            Task.Factory.StartNew(action, System.Threading.CancellationToken.None,
                TaskCreationOptions.None, UnityMainThreadTaskScheduler.Default);
        }
    }
}
