using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using IPA.Utilities.Async;
using UnityEngine.SceneManagement;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 書き出した譜面を SongCore に読み直させる。
    /// </summary>
    /// <remarks>
    /// <c>RefreshSongs</c> は、読み込み中（<c>AreSongsLoading</c>）やプレイ中（GameCore）だと
    /// 何もせずに戻り、<c>SongsLoadedEvent</c> も来ない。そのまま待つと完了が来ないので、
    /// 呼べる状態になるまで少し待ってから呼ぶ。
    /// 他の誰かの読み込みが走っていたときも、それが終わってから自分の分をもう一度走らせる
    /// （その読み込みが、書いたばかりのフォルダを見る前に始まっていたかもしれない）。
    ///
    /// 読み込み中に来た頼みは次の1回にまとめる。全部がメインスレッドから呼ばれる前提。
    /// </remarks>
    internal class SongRefreshService
    {
        /// <summary>呼べる状態になるのを待つ間隔。</summary>
        private const int RetryMs = 500;

        /// <summary>呼べる状態になるまで待つ上限。</summary>
        private const int StartTimeoutMs = 60_000;

        /// <summary>呼んでから完了が来るまで待つ上限。曲数の多い環境の全再読込を見込む。</summary>
        private const int LoadTimeoutMs = 180_000;

        /// <summary>今の読み込みが終わったら知らせる相手。</summary>
        private List<Action<bool>> current = new List<Action<bool>>();

        /// <summary>今の読み込みの後で、もう一度読み込ませる相手。</summary>
        private List<Action<bool>> queued = new List<Action<bool>>();

        private bool currentFull;
        private bool queuedFull;

        /// <summary>RefreshSongs を呼んで完了を待っている間は true。</summary>
        private bool loading;

        /// <summary>待ちの打ち切りが古い読み込みに効かないようにする番号。</summary>
        private int generation;

        private Action<SongCore.Loader, ConcurrentDictionary<string, BeatmapLevel>>? handler;

        /// <summary>読み込みを待っている間は true。</summary>
        internal bool IsRefreshing => current.Count > 0;

        /// <summary>
        /// 曲一覧を更新する。完了したら <paramref name="onDone"/> を呼ぶ（メインスレッド）。
        /// </summary>
        /// <param name="fullRefresh">
        /// false だと差分だけ見る。1本足しただけならこれで足りるうえ、
        /// 曲数の多い環境では全再読込が数十秒かかる。
        /// </param>
        internal void Refresh(Action<bool> onDone, bool fullRefresh = false)
        {
            if (loading)
            {
                // もう走っている読み込みは、今書いたものを見ないかもしれない。次の回に回す
                queued.Add(onDone);
                queuedFull |= fullRefresh;
                return;
            }

            current.Add(onDone);
            currentFull |= fullRefresh;

            // 呼べる状態を待っている最中なら、その回に乗せるだけ
            if (current.Count > 1) return;

            WaitAndStart(++generation, 0);
        }

        private static bool CanStart()
        {
            if (SongCore.Loader.AreSongsLoading) return false;
            return SceneManager.GetActiveScene().name != "GameCore";
        }

        private void WaitAndStart(int run, int waitedMs)
        {
            if (run != generation) return;

            if (!CanStart())
            {
                if (waitedMs >= StartTimeoutMs)
                {
                    Plugin.Log?.Warn("SongCore stayed busy; gave up reloading songs");
                    Finish(false);
                    return;
                }

                Later(RetryMs, () => WaitAndStart(run, waitedMs + RetryMs));
                return;
            }

            handler = (loader, levels) =>
            {
                if (run != generation) return;
                Plugin.LogDebug("songs reloaded");
                Finish(true);
            };

            try
            {
                loading = true;
                SongCore.Loader.SongsLoadedEvent += handler;
                SongCore.Loader.Instance.RefreshSongs(currentFull);
            }
            catch (Exception e)
            {
                // 読み直しに失敗しても譜面自体は書けている。
                // ゲームを入り直せば読まれるので、UI 側はその旨を出すだけでよい
                Plugin.Log?.Error("RefreshSongs failed: " + e);
                Finish(false);
                return;
            }

            Later(LoadTimeoutMs, () =>
            {
                if (run != generation || !loading) return;
                Plugin.Log?.Warn("song reload did not finish in time");
                Finish(false);
            });
        }

        /// <summary>今の回を閉じて知らせる。後ろに待っている頼みがあれば、続けて次の回を始める。</summary>
        private void Finish(bool ok)
        {
            if (handler != null)
            {
                SongCore.Loader.SongsLoadedEvent -= handler;
                handler = null;
            }

            generation++;
            loading = false;

            var done = current;
            current = queued;
            currentFull = queuedFull;
            queued = new List<Action<bool>>();
            queuedFull = false;

            // 次の回を先に始めてから知らせる。知らせた先がまた Refresh を呼んでも、
            // 走っている回（または待っている回）に乗るだけで、同じ回を2度始めない
            if (current.Count > 0)
                WaitAndStart(generation, 0);

            foreach (var callback in done)
            {
                try { callback(ok); }
                catch (Exception e) { Plugin.Log?.Error("song reload callback failed: " + e); }
            }
        }

        private static void Later(int delayMs, Action action)
        {
            Task.Delay(delayMs).ContinueWith(_ => action(), UnityMainThreadTaskScheduler.Default);
        }
    }
}
