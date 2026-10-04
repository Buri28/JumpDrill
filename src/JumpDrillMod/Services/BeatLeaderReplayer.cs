using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using IPA.Loader;
using IPA.Utilities.Async;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 記録のリプレイを BeatLeader のゲーム内リプレイヤーで再生する。
    /// </summary>
    /// <remarks>
    /// <b>BeatLeader は必須にしない。</b> 記録は LocalLeaderboard のリプレイからも読めるので、
    /// BeatLeader が入っていなくてもリーダーボードは動く。再生だけができない。
    ///
    /// BeatLeader の型に触るのは <see cref="StartWithBeatLeader"/> の中だけにしてある。
    /// 入っていない環境でその型を含むメソッドが JIT されると読み込みで例外になるので、
    /// 入っているか確かめてから、別のメソッド（インライン化させない）を呼ぶ。
    ///
    /// 使うのは BeatLeader の公開の口（<c>ReplayDecoder.DecodeReplay</c> と
    /// <c>ReplayerMenuLoader.StartReplayAsync</c>）。譜面を探して場面を切り替え、
    /// 終わったらメニューに戻すところまで BeatLeader がやる。
    /// LocalLeaderboard のリプレイも同じ BSOR 形式なので同じように渡せる。
    /// </remarks>
    internal static class BeatLeaderReplayer
    {
        /// <summary>BeatLeader が読み込まれているか。無ければ再生のボタンを出さない。</summary>
        internal static bool IsAvailable =>
#if JUMPDRILL_BEATLEADER
            PluginManager.GetPluginFromId("BeatLeader") != null;
#else
            false;
#endif

        /// <summary>
        /// 再生を始める。失敗したら理由を <paramref name="onError"/> で返す（メインスレッドで）。
        /// </summary>
        internal static void Play(string? replayPath, Action<string> onError)
        {
            if (!IsAvailable)
            {
                onError("BeatLeader is not installed.");
                return;
            }

            if (string.IsNullOrEmpty(replayPath) || !File.Exists(replayPath))
            {
                onError("The replay file is gone.");
                return;
            }

            try
            {
                StartWithBeatLeader(replayPath!, onError);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not start the replay: " + e);
                onError("Could not start the replay. See the log.");
            }
        }

        /// <remarks>
        /// ここだけが BeatLeader の型を使う。インライン化されると呼び出し元で型が解決されて、
        /// BeatLeader が無い環境で <see cref="Play"/> ごと落ちる。
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StartWithBeatLeader(string replayPath, Action<string> onError)
        {
#if JUMPDRILL_BEATLEADER
            var loader = BeatLeader.Replayer.ReplayerMenuLoader.Instance;
            if (loader == null)
            {
                onError("The BeatLeader replayer is not ready.");
                return;
            }

            BeatLeader.Models.Replay.Replay? replay;
            if (!BeatLeader.Models.Replay.ReplayDecoder.TryDecodeReplay(File.ReadAllBytes(replayPath), out replay) ||
                replay == null)
            {
                onError("Could not read the replay.");
                return;
            }

            // 譜面が見つからない（消した・パックが読まれていない）ときは false が返る
            loader.StartReplayAsync(replay!).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Plugin.Log?.Error("replay failed: " + task.Exception);
                    onError("Could not start the replay. See the log.");
                }
                else if (!task.Result)
                {
                    onError("BeatLeader could not open this drill. Is it still in the pack?");
                }
            }, UnityMainThreadTaskScheduler.Default);
#else
            onError("BeatLeader is not installed.");
#endif
        }
    }
}
