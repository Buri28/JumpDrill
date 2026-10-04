using System;
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
        /// 書き始める。<paramref name="onProgress"/> と <paramref name="onDone"/> はメインスレッドで呼ばれる。
        /// </summary>
        /// <param name="onProgress">1本書くごとに（書いた数, 全体）。</param>
        /// <param name="onDone">終わったら（成功したか, 画面に出す1行）。</param>
        internal void Start(Action<int, int> onProgress, Action<bool, string> onDone)
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
                // 既にあるものは書き直さない。ID は設定から決まるので同じ名前なら中身も同じ。
                // 途中でやめても、次は続きから書ける
                DrillSetWriter.WriteAll(root, new LevelWriteOptions { Format = AudioFormat.Ogg },
                    (done, total, name) => OnMainThread(() => onProgress(done, total)),
                    skipExisting: true);
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
                // IsRunning を落とすのは読み込みが終わってから
                refresher.Refresh(ok =>
                {
                    IsRunning = false;
                    onDone(true, ok
                        ? "The drills are ready."
                        : "Written, but the song list did not reload. Restart the game.");
                }, fullRefresh: false);
            }, UnityMainThreadTaskScheduler.Default);
        }

        private static void OnMainThread(Action action)
        {
            Task.Factory.StartNew(action, System.Threading.CancellationToken.None,
                TaskCreationOptions.None, UnityMainThreadTaskScheduler.Default);
        }
    }
}
