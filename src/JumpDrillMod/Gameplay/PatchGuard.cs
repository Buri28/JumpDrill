using System;

namespace JumpDrillMod.Gameplay
{
    /// <summary>
    /// 毎フレーム・毎ノーツ呼ばれる差し込み（Harmony のパッチ）が落ちたときの始末。
    /// </summary>
    /// <remarks>
    /// ゲームの版が合わないなどで1度落ちると、同じ例外が毎フレーム出続けてログが膨らみ、
    /// ゲームも止まる。最初の1回だけログに書き、以後はゲームを再起動するまで何もしない。
    /// </remarks>
    internal static class PatchGuard
    {
        internal static bool Failed { get; private set; }

        internal static void Fail(string where, Exception e)
        {
            if (Failed) return;
            Failed = true;
            Plugin.Log?.Error(where + " failed; replay recording is off until the game restarts: " + e);
        }
    }
}
