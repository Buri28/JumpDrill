using System;
using System.Reflection;
using System.Threading;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 曲一覧に読まれている譜面の <c>.dat</c> を、このMODが直接読むための許可。
    /// </summary>
    /// <remarks>
    /// 1.44 向けの SongCore（3.16）は、読み込み済みの譜面の難易度ファイルを直接読むと
    /// 例外にする（<c>IOBlacklistHooks</c>。<c>FileStream.Read</c> に掛けてあるので読み方を変えても同じ）。
    /// 同じところに、読んでよい間だけ立てる印（<c>AllowIO</c>）が用意されているので、それを使う。
    /// 読むのはこのMODが書いたドリルの譜面で、ノーツ数を数えるだけ。
    ///
    /// 古い SongCore にはこの仕組みが無い。そのときは何もせずにそのまま読む。
    /// </remarks>
    internal static class SongCoreFileAccess
    {
        private static readonly AsyncLocal<bool>? allowIO = FindAllowIO();

        internal static T Run<T>(Func<T> read)
        {
            if (allowIO == null) return read();

            bool previous = allowIO.Value;
            allowIO.Value = true;
            try
            {
                return read();
            }
            finally
            {
                allowIO.Value = previous;
            }
        }

        private static AsyncLocal<bool>? FindAllowIO()
        {
            try
            {
                var type = typeof(SongCore.Loader).Assembly.GetType("SongCore.Hooks.BeatmapLevelCache.IOBlacklistHooks", false);
                var property = type?.GetProperty("AllowIO", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return property?.GetValue(null) as AsyncLocal<bool>;
            }
            catch (Exception e)
            {
                Plugin.LogDebug("could not find SongCore's AllowIO: " + e.Message);
                return null;
            }
        }
    }
}
