using System;
using System.Linq;
using System.Reflection;
using BeatSaberMarkupLanguage;
using HMUI;
using JumpDrillMod.Services;
using JumpDrillMod.UI.Leaderboard;
using UnityEngine;

namespace JumpDrillMod.UI.Medals
{
    /// <summary>
    /// ドリルを選んで叩く画面。いま出ている画面（メインメニューか曲選択）の上に重ねて出す。
    /// </summary>
    /// <remarks>
    /// 真ん中に Drill（ドリルセットのメダル）/ Other（それ以外のドリル）、
    /// 右に<b>選んだドリルのリーダーボード</b>（記録と軌道の図）を出す。
    /// 右は曲選択の枠に出しているものと同じビュー（<see cref="JumpDrillLeaderboardView"/>）で、
    /// 叩く前にその譜面のこれまでを見てから選べる。
    ///
    /// 表が 14 行 × 8 列あり、Mods タブやリーダーボードの枠には収まらないので、
    /// 画面をまるごと1枚使う。閉じれば開いた元の画面に戻る。
    /// Play はこの画面からそのまま始め、終わるとこの画面に戻る。
    /// クリアか失敗で終わったときは、本体の結果画面（練習の扱い）を挟む。
    /// </remarks>
    internal class MedalFlowCoordinator : FlowCoordinator
    {
        private MedalViewController? view;
        private JumpDrillLeaderboardView? board;
        private LevelLauncher? launcher;
        private ReplayScoreReader? reader;
        private DiagramService? diagrams;

        /// <summary>
        /// 自分を出した親。閉じるときに要る。
        /// HMUI の <c>_parentFlowCoordinator</c> は外から見えないので、出すときに覚えておく。
        /// </summary>
        private FlowCoordinator? parent;

        internal void Setup(ReplayScoreReader reader, LevelLauncher launcher, IconService icons, DiagramService diagrams,
            DrillSetGenerationService drillSet)
        {
            this.launcher = launcher;
            this.reader = reader;
            this.diagrams = diagrams;

            if (view == null) view = BeatSaberUI.CreateViewController<MedalViewController>();
            view.Setup(reader, launcher, icons, drillSet, Play, ShowRecords);

            // 曲選択の枠に出しているものとは別の実体にする。1つのビューを2つの画面には出せない
            if (board == null) board = BeatSaberUI.CreateViewController<JumpDrillLeaderboardView>();
        }

        /// <summary>曲選択の上に出す。いま一番手前にある画面の上に積む。</summary>
        internal void Open()
        {
            parent = BeatSaberUI.MainFlowCoordinator.YoungestChildFlowCoordinatorOrSelf();
            parent.PresentFlowCoordinator(this);
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("Jump Drill");
                showBackButton = true;
            }

            if (addedToHierarchy && view != null)
                ProvideInitialViewControllers(view, rightScreenViewController: board);

            // 2回目以降に開いたときは、その間に叩いたぶんを読み直す
            if (!firstActivation) view?.Rescan();
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            if (removedFromHierarchy) ReleaseResults();
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            // 結果画面を出している間は、戻るで結果だけを閉じる
            if (results != null && topViewController == results)
            {
                ContinueFromResults(results);
                return;
            }

            Close(null);
        }

        /// <summary>
        /// 選んだドリルの記録を右の枠に出す。
        /// </summary>
        /// <remarks>
        /// 1本ぶんなので、その ID のリプレイだけを読む。解析結果はファイル単位で持っているので、
        /// 真ん中の表を出すときに読んだものは読み直さない。
        /// </remarks>
        private void ShowRecords(string? drillId)
        {
            if (board == null || reader == null || diagrams == null) return;

            board.SetScores(reader.For(drillId, PluginConfig.Instance?.PackName ?? "JumpDrill"), diagrams);
        }

        /// <summary>
        /// そのドリルをその場で始める。
        /// </summary>
        /// <remarks>
        /// この画面は閉じない。叩き終えるとメニューはこの画面のまま戻ってくるので、
        /// 表と右の記録を読み直して、いま叩いた分を出す。
        /// </remarks>
        private void Play(string drillId)
        {
            if (launcher == null) return;

            string? error = launcher.Start(drillId, Finished(drillId));

            if (error != null) view?.SetStatus(error);
        }

        /// <summary>
        /// 叩き終えてこの画面に戻ったところ。表と右の記録を読み直し、結果画面を出す。
        /// </summary>
        /// <remarks>
        /// 読み直すのはリプレイを書き終えてから。書き出しは別スレッドで、
        /// メニューに戻った時点ではまだ終わっていないことがある。
        /// 右の記録は Rescan が選んでいるドリル（いま叩いたもの）で出し直す。
        /// </remarks>
        private Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults> Finished(string drillId)
        {
            return (setup, completion) =>
            {
                Gameplay.DrillReplayRecorder.PendingSave.ContinueWith(
                    _ => view?.Rescan(),
                    IPA.Utilities.Async.UnityMainThreadTaskScheduler.Default);

                ShowResults(drillId, setup, completion);
            };
        }

        // ───────── 結果画面 ─────────

        /// <summary>出している本体の結果画面。出していなければ null。</summary>
        private ResultsViewController? results;

        /// <summary>結果画面のドリル。Restart で同じものを始める。</summary>
        private string? resultsDrillId;

        /// <summary>結果画面のボタンに元から付いていた受け手。閉じるときに戻す。</summary>
        private Delegate? savedContinue;
        private Delegate? savedRestart;

        private const BindingFlags InstanceField = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo? ContinueEvent =
            typeof(ResultsViewController).GetField("continueButtonPressedEvent", InstanceField);

        private static readonly FieldInfo? RestartEvent =
            typeof(ResultsViewController).GetField("restartButtonPressedEvent", InstanceField);

        /// <summary>
        /// 本体の結果画面を、この画面の上に出す。曲選択の後に出るものと同じ（練習の扱い）。
        /// </summary>
        /// <remarks>
        /// 出すのはクリアか失敗のとき（本体と同じ）。途中でやめたときはそのままこの画面に戻る。
        ///
        /// 結果画面は本体に1つしかなく、曲選択（<c>SoloFreePlayFlowCoordinator</c>）が
        /// ボタンの受け手を付けたままにしている。この画面を曲選択から開いていると、
        /// 押したときに曲選択の側も動いて、Restart なら曲選択で選んでいる曲を始めてしまう。
        /// 出している間だけ受け手をこの画面のものに差し替え、閉じたら元に戻す。
        /// 差し替えられなければ（版が違って名前が変わったなど）結果画面は出さない。
        /// </remarks>
        private void ShowResults(string drillId, StandardLevelScenesTransitionSetupDataSO setup,
            LevelCompletionResults completion)
        {
            var state = completion.levelEndStateType;
            if (state != LevelCompletionResults.LevelEndStateType.Cleared &&
                state != LevelCompletionResults.LevelEndStateType.Failed) return;
            if (!isActivated || results != null) return;

            if (ContinueEvent == null || RestartEvent == null)
            {
                Plugin.Log?.Warn("results screen: could not find its button events; not showing it");
                return;
            }

            var screen = Resources.FindObjectsOfTypeAll<ResultsViewController>().FirstOrDefault();
            if (screen == null)
            {
                Plugin.Log?.Warn("results screen: not found");
                return;
            }

            try
            {
                var key = setup.beatmapKey;
                screen.Init(completion, setup.transformedBeatmapData, in key, setup.beatmapLevel,
                    setup.practiceSettings != null, false);

                savedContinue = ContinueEvent.GetValue(screen) as Delegate;
                savedRestart = RestartEvent.GetValue(screen) as Delegate;
                ContinueEvent.SetValue(screen, (Action<ResultsViewController>)ContinueFromResults);
                RestartEvent.SetValue(screen, (Action<ResultsViewController>)RestartFromResults);

                results = screen;
                resultsDrillId = drillId;
                PresentViewController(screen, null, ViewController.AnimationDirection.Horizontal, true);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not show the results screen: " + e);
                ReleaseResults();
            }
        }

        private void ContinueFromResults(ResultsViewController screen)
        {
            if (topViewController == screen) DismissViewController(screen);
            ReleaseResults();
        }

        /// <summary>同じドリルをもう一度始める。結果画面は場面が切り替わる直前に閉じる（本体と同じ）。</summary>
        private void RestartFromResults(ResultsViewController screen)
        {
            if (resultsDrillId == null) return;

            string drillId = resultsDrillId;
            string? error = launcher?.Start(drillId, Finished(drillId),
                () =>
                {
                    if (topViewController == screen)
                        DismissViewController(screen, ViewController.AnimationDirection.Horizontal, null, true);
                    ReleaseResults();
                });

            if (error != null)
            {
                ContinueFromResults(screen);
                view?.SetStatus(error);
            }
        }

        /// <summary>結果画面のボタンの受け手を元に戻す。</summary>
        private void ReleaseResults()
        {
            var screen = results;
            results = null;
            resultsDrillId = null;
            if (screen == null) return;

            try
            {
                ContinueEvent?.SetValue(screen, savedContinue);
                RestartEvent?.SetValue(screen, savedRestart);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not restore the results screen: " + e);
            }

            savedContinue = null;
            savedRestart = null;
        }

        private void Close(System.Action? finished)
        {
            if (parent == null) return;
            parent.DismissFlowCoordinator(this, finishedCallback: finished);
        }
    }
}
