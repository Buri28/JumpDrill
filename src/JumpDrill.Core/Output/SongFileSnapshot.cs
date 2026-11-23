using System;
using System.Collections.Generic;
using System.IO;

namespace JumpDrill.Output
{
    /// <summary>
    /// 譜面フォルダの音のファイル（大きさと更新時刻）を控え、前に控えたときから書き換わったものを求める。
    /// </summary>
    /// <remarks>
    /// ゲームは音をファイルのパスで覚えていて、同じ譜面を続けて叩くと、上書きして読み直した後も
    /// 前の音を使い続けることがある。書き換わった音のパスを求め、MOD がゲームの覚えから外すのに使う。
    /// </remarks>
    public static class SongFileSnapshot
    {
        /// <summary>譜面の音として見る拡張子。</summary>
        public static readonly IReadOnlyList<string> AudioExtensions = new[] { ".ogg", ".egg", ".wav" };

        /// <summary>
        /// フォルダごとの音のファイルを控える。鍵は <c>Path.Combine(フォルダ, ファイル名)</c>（フォルダの書き方はそのまま）。
        /// 無いフォルダ・読めないファイルは飛ばす。
        /// </summary>
        public static Dictionary<string, string> Take(IEnumerable<string> folders)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (folders == null) return map;

            foreach (var folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                string[] files;
                try { files = Directory.GetFiles(folder); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                foreach (var file in files)
                {
                    if (!IsAudio(file)) continue;
                    try
                    {
                        var info = new FileInfo(file);
                        map[Path.Combine(folder, info.Name)] = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }

            return map;
        }

        /// <summary>
        /// <paramref name="before"/> から書き換わった、または新しくできた音のパス。
        /// <paramref name="before"/> が null（まだ控えていない）なら空。
        /// </summary>
        public static List<string> Changed(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> now)
        {
            var changed = new List<string>();
            if (before == null || now == null) return changed;

            foreach (var entry in now)
                if (!before.TryGetValue(entry.Key, out var old) || old != entry.Value)
                    changed.Add(entry.Key);

            return changed;
        }

        private static bool IsAudio(string file)
        {
            string ext = Path.GetExtension(file);
            foreach (var audio in AudioExtensions)
                if (string.Equals(ext, audio, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
