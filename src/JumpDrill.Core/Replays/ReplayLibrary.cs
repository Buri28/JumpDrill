using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 保存済みのリプレイを集める。
    ///
    /// BeatLeader と LocalLeaderboard が同じプレイをそれぞれ保存するので、
    /// 素直に集めると同じものが2件出る。ファイル名の頭が同じなら同一とみなす
    /// （LocalLeaderboard は末尾に独自の連番を足すだけ）。
    ///
    /// JumpDrillMod も自分でリプレイを残す（BeatLeader が無い、または練習の保存を切っている環境のため）。
    /// BeatLeader も同じプレイを残していれば、そちらを採って自分の分は数えない。
    /// </summary>
    public static class ReplayLibrary
    {
        /// <summary>
        /// リプレイを置いているMOD。<c>UserData\&lt;名前&gt;\Replays</c> にある。
        /// 同じプレイが複数あれば、この並びで前にあるものを採る。
        /// </summary>
        public static readonly string[] ReplayMods = { "BeatLeader", "LocalLeaderboard", OwnReplayMod };

        /// <summary>JumpDrillMod が自分で残すリプレイの置き場の名前。</summary>
        public const string OwnReplayMod = "JumpDrillMod";

        /// <summary>
        /// JumpDrillMod が自分で残したリプレイの、ファイル名の先頭。
        /// BeatLeader ではここにプレイヤー ID が入る。
        /// </summary>
        public const string OwnPlayerId = "JumpDrill";

        /// <summary>
        /// 同じプレイとみなす開始時刻のずれ（秒）。ファイル名の最後の数字がプレイを始めた時刻で、
        /// BeatLeader と JumpDrillMod はどちらも同じ場面ができたところで刻むので、ずれは 1 秒以内。
        /// 広くとると、すぐにやり直した別のプレイまで同じとみなしてしまう。
        /// </summary>
        public const int SamePlaySeconds = 3;

        /// <summary>リプレイが置かれているフォルダを探す。</summary>
        /// <param name="installRoot">
        /// この Beat Saber（Beat Saber.exe のあるフォルダ）のものだけにする。null なら見つかったもの全部。
        /// </param>
        public static List<string> FindReplayFolders(string installRoot = null)
        {
            var folders = new List<string>();
            string only = installRoot == null ? null : Path.GetFullPath(installRoot).TrimEnd('\\', '/');

            foreach (var level in Output.LevelWriter.FindLevelFolders())
            {
                if (string.IsNullOrEmpty(level.InstallRoot)) continue;
                if (only != null && !string.Equals(Path.GetFullPath(level.InstallRoot).TrimEnd('\\', '/'), only, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var mod in ReplayMods)
                {
                    string path = Path.Combine(level.InstallRoot, "UserData", mod, "Replays");
                    if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                        folders.Add(path);
                }
            }

            return folders;
        }

        /// <summary>
        /// リプレイのファイル一覧。重複は落とす。
        /// </summary>
        /// <param name="songNameFilter">
        /// 曲名に含まれていなければ除く。null なら全部。
        /// 自作ドリルだけ見たいときに "Drill" を渡す。
        /// </param>
        /// <param name="installRoot">この Beat Saber のものだけにする。null なら全部。</param>
        public static List<string> FindReplayFiles(string songNameFilter = null, string installRoot = null)
        {
            var all = new List<string>();

            foreach (var folder in FindReplayFolders(installRoot))
            {
                string[] files;
                try { files = Directory.GetFiles(folder, "*.bsor"); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (songNameFilter != null &&
                        name.IndexOf(songNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    all.Add(file);
                }
            }

            return RemoveDuplicates(all).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        }

        /// <summary>
        /// 同じプレイのリプレイを1つにする。前にあるものを優先する
        /// （<see cref="ReplayMods"/> の順に並べて渡す）。
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        ///   <item>BeatLeader と LocalLeaderboard の組は、ファイル名が末尾の連番を除いて同じ（<see cref="NormalizeName"/>）</item>
        ///   <item>JumpDrillMod が残したものは、譜面（ハッシュ・難易度・モード）が同じで開始時刻が
        ///         <see cref="SamePlaySeconds"/> 秒以内のものが他にあれば落とす。
        ///         プレイヤー ID が違うので名前では揃わない。
        ///         他の1件が落とすのは1件だけ（いちばん近いもの）。やり直しを続けた別のプレイを巻き込まない</item>
        /// </list>
        /// </remarks>
        public static List<string> RemoveDuplicates(IEnumerable<string> files)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            foreach (var file in files)
            {
                string key = NormalizeName(Path.GetFileNameWithoutExtension(file));
                if (byName.ContainsKey(key)) continue;
                byName[key] = file;
                order.Add(file);
            }

            // 他のMODが残したプレイ。譜面ごとに開始時刻を並べておく
            var others = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in order)
            {
                if (IsOwnRecording(file)) continue;

                string map;
                long started;
                if (!TryParsePlay(file, out map, out started)) continue;

                List<long> times;
                if (!others.TryGetValue(map, out times)) others[map] = times = new List<long>();
                times.Add(started);
            }

            // 自分の分を、近いものから順に他の1件と組にする。組になったものが重複
            var candidates = new List<Tuple<long, string, string, long>>();   // (ずれ, ファイル, 譜面, 他の時刻)
            foreach (var file in order)
            {
                if (!IsOwnRecording(file)) continue;

                string map;
                long started;
                List<long> times;
                if (!TryParsePlay(file, out map, out started) || !others.TryGetValue(map, out times)) continue;

                foreach (long t in times)
                {
                    long gap = Math.Abs(t - started);
                    if (gap <= SamePlaySeconds) candidates.Add(Tuple.Create(gap, file, map, t));
                }
            }

            var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in candidates.OrderBy(c => c.Item1))
            {
                string other = c.Item3 + "@" + c.Item4;
                if (dropped.Contains(c.Item2) || used.Contains(other)) continue;
                dropped.Add(c.Item2);
                used.Add(other);
            }

            return order.Where(file => !dropped.Contains(file)).ToList();
        }

        /// <summary>
        /// 同じプレイのリプレイか。<see cref="RemoveDuplicates"/> と同じ見方で、
        /// 名前が揃うか、譜面が同じで開始時刻が <see cref="SamePlaySeconds"/> 秒以内なら同じとみなす。
        /// </summary>
        /// <returns>同じなら開始時刻のずれ（秒）。違えば -1。</returns>
        public static long SamePlayGap(string a, string b)
        {
            if (string.Equals(NormalizeName(Path.GetFileNameWithoutExtension(a)),
                              NormalizeName(Path.GetFileNameWithoutExtension(b)), StringComparison.OrdinalIgnoreCase))
                return 0;

            string mapA, mapB;
            long startA, startB;
            if (!TryParsePlay(a, out mapA, out startA) || !TryParsePlay(b, out mapB, out startB)) return -1;
            if (!string.Equals(mapA, mapB, StringComparison.OrdinalIgnoreCase)) return -1;

            long gap = Math.Abs(startA - startB);
            return gap <= SamePlaySeconds ? gap : -1;
        }

        /// <summary>JumpDrillMod が自分で残したリプレイか（置き場のフォルダで見る）。</summary>
        public static bool IsOwnRecording(string file)
        {
            string replays = Path.GetDirectoryName(file);
            string mod = replays == null ? null : Path.GetFileName(Path.GetDirectoryName(replays));
            return string.Equals(mod, OwnReplayMod, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// ファイル名から譜面とプレイを始めた時刻を読む。
        /// 名前は BeatLeader の形 <c>ID[-practice][-fail][-exit]-曲名-難易度-モード-ハッシュ-時刻</c>。
        /// 曲名には - が入り得るので、後ろから数える。
        /// </summary>
        private static bool TryParsePlay(string file, out string map, out long started)
        {
            map = null;
            started = 0;

            string[] parts = NormalizeName(Path.GetFileNameWithoutExtension(file)).Split('-');
            if (parts.Length < 5) return false;

            int n = parts.Length;
            if (!long.TryParse(parts[n - 1], out started)) return false;

            map = parts[n - 4] + "-" + parts[n - 3] + "-" + parts[n - 2];
            return true;
        }

        /// <summary>
        /// JumpDrillMod が残すリプレイのファイル名。BeatLeader と同じ形にして、
        /// プレイヤー ID の代わりに <see cref="OwnPlayerId"/> を入れる。
        /// </summary>
        public static string OwnFileName(ReplayInfo info, bool exited)
        {
            string name = OwnPlayerId
                + (info.Speed != 0f ? "-practice" : "")
                + (info.FailTime != 0f ? "-fail" : "")
                + (exited ? "-exit" : "")
                + "-" + info.SongName + "-" + info.Difficulty + "-" + info.Mode + "-" + info.Hash + "-" + info.Timestamp
                + ".bsor";

            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            return new Regex("[" + Regex.Escape(invalid) + "]").Replace(name, "_");
        }

        /// <summary>
        /// 同じプレイをまとめるための鍵。
        ///
        /// LocalLeaderboard は BeatLeader と同じ名前の末尾に "_&lt;長い数字&gt;" を足すだけ。
        /// 単純に最後の "_" で切ると、曲名に含まれる "_"（R_8_b など）で切れてしまうので、
        /// 末尾が数字の並びのときだけ落とす。
        /// </summary>
        public static string NormalizeName(string fileNameWithoutExtension)
        {
            if (string.IsNullOrEmpty(fileNameWithoutExtension)) return fileNameWithoutExtension;

            int underscore = fileNameWithoutExtension.LastIndexOf('_');
            if (underscore <= 0) return fileNameWithoutExtension;

            string tail = fileNameWithoutExtension.Substring(underscore + 1);
            if (tail.Length < 6) return fileNameWithoutExtension;

            foreach (char c in tail)
                if (c < '0' || c > '9') return fileNameWithoutExtension;

            return fileNameWithoutExtension.Substring(0, underscore);
        }

        /// <summary>
        /// JumpDrill が作った譜面のリプレイだけに絞るための語。
        /// 末尾の空白まで含めないと "Cendrillon" のような曲名にも当たる。
        /// </summary>
        public const string DrillFilter = "Drill ";

        /// <summary>読めなかったものは飛ばして、読めたぶんだけ返す。</summary>
        public static List<ReplayScore> LoadScores(IEnumerable<string> files, Action<string, Exception> onError = null)
        {
            var results = new List<ReplayScore>();

            foreach (var file in files)
            {
                try
                {
                    results.Add(SwingAnalyzer.Analyze(ReplayReader.Read(file)));
                }
                catch (Exception ex)
                {
                    if (onError != null) onError(file, ex);
                }
            }

            return results;
        }
    }
}
