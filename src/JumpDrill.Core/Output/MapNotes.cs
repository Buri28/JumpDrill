using System;
using System.Collections.Generic;
using System.IO;

namespace JumpDrill.Output
{
    /// <summary>
    /// 譜面のノーツ数を数える。<b>満点の分母はここから取る。</b>
    ///
    /// リプレイからは数えられない。途中でやめると、そこから先のノーツは
    /// スポーンせず記録にも残らないので、片手ぶん丸ごと抜けることがある。
    /// 実測でも「右30秒 → 左30秒」のドリルを右で切り上げた記録に、
    /// 左のノーツが Good も Miss も 0 件しか入っていなかった。
    /// それを満点にすると<b>片手しか振っていない記録が満点扱い</b>になる。
    /// </summary>
    public static class MapNotes
    {
        /// <summary>色ごとのノーツ数。0 = 左（赤）、1 = 右（青）。</summary>
        public struct Counts
        {
            public int Left;
            public int Right;

            /// <summary>
            /// 左手の振りの数。同じ手・同じ時刻のノーツ（タワー・スタック）を1つと数えたもの。
            /// 普通のドリルでは <see cref="Left"/> と同じ。
            /// </summary>
            public int SwingLeft;

            /// <summary>右手の振りの数。</summary>
            public int SwingRight;

            public int Total { get { return Left + Right; } }

            public int For(int saberType) { return saberType == 0 ? Left : Right; }
        }

        /// <summary>
        /// 譜面フォルダの難易度ファイルからノーツを数える。読めなければ false。
        /// </summary>
        public static bool TryCount(string levelFolder, out Counts counts)
        {
            counts = new Counts();
            if (string.IsNullOrEmpty(levelFolder)) return false;

            string path = FindDifficulty(levelFolder);
            if (path == null) return false;

            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }

            return TryCountText(text, out counts);
        }

        /// <summary>
        /// 難易度ファイルの中身から数える。
        ///
        /// <c>_events</c> にも <c>_type</c> があるので、<b><c>_notes</c> の配列の中だけ</b>を見る。
        /// 爆弾（<c>_type</c> 3）は振る相手ではないので数えない。
        /// 振りの数は <c>_time</c> と <c>_type</c> が同じものを1つにまとめて数える。
        /// </summary>
        public static bool TryCountText(string text, out Counts counts)
        {
            counts = new Counts();
            if (string.IsNullOrEmpty(text)) return false;

            const string Key = "\"_notes\"";

            int key = text.IndexOf(Key, StringComparison.Ordinal);
            if (key < 0) return false;

            int open = text.IndexOf('[', key + Key.Length);
            if (open < 0) return false;

            int close = text.IndexOf(']', open + 1);
            if (close < 0) return false;

            string notes = text.Substring(open + 1, close - open - 1);

            var swings = new HashSet<string>(StringComparer.Ordinal);

            // ノーツ1つは { … } 1つ。入れ子は無い。
            for (int start = notes.IndexOf('{'); start >= 0; start = notes.IndexOf('{', start + 1))
            {
                int end = notes.IndexOf('}', start + 1);
                if (end < 0) break;
                string note = notes.Substring(start + 1, end - start - 1);

                string type = ValueOf(note, "\"_type\"");
                if (type != "0" && type != "1") continue;

                if (type == "0") counts.Left++;
                else counts.Right++;

                if (swings.Add(type + "|" + ValueOf(note, "\"_time\"")))
                {
                    if (type == "0") counts.SwingLeft++;
                    else counts.SwingRight++;
                }
            }

            return counts.Total > 0;
        }

        /// <summary>1ノーツの中の、キーに続く値の字面。無ければ空。</summary>
        private static string ValueOf(string note, string key)
        {
            int i = note.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "";

            int at = i + key.Length;
            while (at < note.Length && (note[at] == ' ' || note[at] == ':')) at++;

            int end = at;
            while (end < note.Length && note[end] != ',' && note[end] != '}' && !char.IsWhiteSpace(note[end])) end++;
            return note.Substring(at, end - at);
        }

        /// <summary>難易度ファイルを探す。JumpDrill が書くのは1つだけ。</summary>
        private static string FindDifficulty(string levelFolder)
        {
            string standard = Path.Combine(levelFolder, Serialization.BeatmapSerializer.DifficultyFileName);
            if (File.Exists(standard)) return standard;

            string[] candidates;
            try { candidates = Directory.GetFiles(levelFolder, "*.dat"); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }

            foreach (var candidate in candidates)
            {
                if (string.Equals(Path.GetFileName(candidate), "Info.dat", StringComparison.OrdinalIgnoreCase)) continue;
                return candidate;
            }

            return null;
        }
    }
}
