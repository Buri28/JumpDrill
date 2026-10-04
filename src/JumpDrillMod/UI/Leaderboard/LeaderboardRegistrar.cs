#if JUMPDRILL_LEADERBOARDCORE
using System;
using JumpDrillMod.Services;
using LeaderboardCore.Managers;
using Zenject;

namespace JumpDrillMod.UI.Leaderboard
{
    /// <summary>
    /// JumpDrill のリーダーボードを LeaderboardCore に登録する。
    /// </summary>
    /// <remarks>
    /// 登録は曲選択の枠が組まれるより前に済んでいる必要があるので、
    /// メニュー・スコープの <see cref="IInitializable"/> で行う。
    /// </remarks>
    internal class LeaderboardRegistrar : IInitializable, IDisposable
    {
        private readonly CustomLeaderboardManager manager;
        private readonly ReplayScoreReader reader;
        private readonly DiagramService diagrams;
        private readonly Medals.DrillSetScreen drillSetScreen;
        private readonly DiContainer container;

        private JumpDrillLeaderboard? leaderboard;

        internal LeaderboardRegistrar(CustomLeaderboardManager manager, ReplayScoreReader reader, DiagramService diagrams,
            Medals.DrillSetScreen drillSetScreen, DiContainer container)
        {
            this.manager = manager;
            this.reader = reader;
            this.diagrams = diagrams;
            this.drillSetScreen = drillSetScreen;
            this.container = container;
        }

        public void Initialize()
        {
            try
            {
                Register();
                Plugin.LogDebug("leaderboard registered");
            }
            catch (Exception e)
            {
                // 登録できなくても譜面の生成と記録は動く
                leaderboard?.Release();
                leaderboard = null;
                Plugin.Log?.Error("could not register the leaderboard: " + e);
            }
        }

        /// <remarks>
        /// <see cref="JumpDrillLeaderboard"/> は LeaderboardCore の型を継承している。LeaderboardCore の版が合わず
        /// 型が読めないと、その型を含むメソッドを JIT する時点で落ちる。<see cref="Initialize"/> に直接書くと
        /// try の外で落ちてメニューの初期化ごと止めるので、別のメソッドに分けて try の中から呼ぶ。
        /// </remarks>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void Register()
        {
            var created = new JumpDrillLeaderboard(reader, diagrams, drillSetScreen, LastLeaderboardSetting.Find(container));
            leaderboard = created;
            manager.Register(created);
        }

        public void Dispose()
        {
            if (leaderboard == null) return;

            try
            {
                leaderboard.Release();
                manager.Unregister(leaderboard);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not unregister the leaderboard: " + e);
            }
            finally
            {
                leaderboard = null;
            }
        }
    }
}
#endif
