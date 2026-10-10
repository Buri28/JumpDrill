using System;
using System.Reflection;
using IPA.Loader;

namespace JumpDrillMod.Gameplay
{
    /// <summary>
    /// いまの曲が、誰かのリプレイの再生か。
    /// </summary>
    /// <remarks>
    /// 再生中もゲームは普通のプレイと同じ場面で動くので、見分けないと
    /// 再生を新しいプレイとして記録してしまう。
    /// どのMODも公開の API を持たないので、各MODが持っている印を名前で読む。
    /// 読めなかったら（MODが無い、版が違って名前が変わった）再生ではないとみなす。
    /// </remarks>
    internal static class ReplayPlaybackCheck
    {
        /// <summary>（プラグイン ID, 型の完全名, 静的な bool のメンバー名）</summary>
        private static readonly (string Plugin, string Type, string Member)[] Flags =
        {
            ("BeatLeader", "BeatLeader.Replayer.ReplayerLauncher", "IsStartedAsReplay"),
            ("ScoreSaber", "ScoreSaber.Features.Replays.ReplayStateRegistry", "IsPlaybackEnabled"),
            ("LocalLeaderboard", "LocalLeaderboard.AffinityPatches.ExtraSongData", "IsLocalLeaderboardReplay"),
        };

        internal static bool IsPlayingBack() => PlayingBackBy() != null;

        /// <summary>再生中だと言っている印（型名.メンバー名）。再生中でなければ null。</summary>
        internal static string? PlayingBackBy()
        {
            foreach (var (plugin, type, member) in Flags)
            {
                if (ReadFlag(plugin, type, member)) return type + "." + member;
            }
            return null;
        }

        private static bool ReadFlag(string pluginId, string typeName, string member)
        {
            try
            {
                var plugin = PluginManager.GetPluginFromId(pluginId);
                if (plugin?.Assembly == null) return false;

                var type = plugin.Assembly.GetType(typeName, false);
                if (type == null) return false;

                const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                var property = type.GetProperty(member, Any);
                if (property != null && property.PropertyType == typeof(bool))
                    return (bool)property.GetValue(null);

                var field = type.GetField(member, Any);
                if (field != null && field.FieldType == typeof(bool))
                    return (bool)field.GetValue(null);
            }
            catch (Exception e)
            {
                Plugin.LogDebug($"could not read {typeName}.{member}: {e.Message}");
            }

            return false;
        }
    }
}
