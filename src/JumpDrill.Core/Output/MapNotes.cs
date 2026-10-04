using System;
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

            const string TypeKey = "\"_type\"";

            for (int i = notes.IndexOf(TypeKey, StringComparison.Ordinal); i >= 0;
                 i = notes.IndexOf(TypeKey, i + TypeKey.Length, StringComparison.Ordinal))
            {
                int at = i + TypeKey.Length;
                while (at < notes.Length && (notes[at] == ' ' || notes[at] == ':')) at++;
                if (at >= notes.Length) break;

                if (notes[at] == '0') counts.Left++;
                else if (notes[at] == '1') counts.Right++;
            }

            return counts.Total > 0;
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
