using System;
using System.Collections.Generic;
using System.IO;
using JumpDrill.Model;

namespace JumpDrill.Output
{
    /// <summary>
    /// 書き出した<b>ドリルの譜面</b>を置き場所から拾い集める。
    ///
    /// JumpDrill が書く先は「作業フォルダの out」と、インストール先の
    /// CustomLevels / CustomWIPLevels / 登録した専用パック。
    /// プレビューもリプレイの譜面探しも相手は自分が作ったドリルなので、
    /// 探し方はここ1か所にまとめる。
    /// </summary>
    public static class DrillLibrary
    {
        /// <summary>見つかったドリル1本。</summary>
        public sealed class DrillLevel
        {
            /// <summary>譜面フォルダ。</summary>
            public string Path { get; internal set; }

            /// <summary>Info.dat の <c>_songName</c>。</summary>
            public string SongName { get; internal set; }

            /// <summary>置き場所の呼び名。同じ譜面が複数の場所にあるので要る。</summary>
            public string Where { get; internal set; }

            /// <summary>書き出した時刻。新しい順に並べるのに使う。</summary>
            public DateTime Written { get; internal set; }

            public override string ToString() { return SongName; }
        }

        /// <summary>探し場所とその呼び名。</summary>
        public sealed class DrillRoot
        {
            public string Path { get; internal set; }

            /// <summary>一覧に出す短い名前。「Steam / Wip」「out」など。</summary>
            public string Label { get; internal set; }
        }

        /// <summary>
        /// ドリルを探す場所。作業フォルダの out を先に見る
        /// （生成したてはまずそこにあるので、直近のものが上に来やすい）。
        /// </summary>
        /// <param name="extraRoots">GUI の出力先など、追加で見たい場所。</param>
        public static List<DrillRoot> Roots(string workspaceRoot, params string[] extraRoots)
        {
            var roots = new List<DrillRoot>();

            Add(roots, new Workspace(workspaceRoot).OutFolder, Lang.T("作業フォルダ", "Workspace"));

            if (extraRoots != null)
                foreach (var extra in extraRoots) Add(roots, extra, Lang.T("出力先", "Output"));

            foreach (var level in LevelWriter.FindLevelFolders())
                Add(roots, level.Path, level.PackName ?? (level.InstallName + " / " + level.Kind));

            return roots;
        }

        /// <summary>
        /// 置き場所にあるドリルを新しい順に集める。
        /// 同じフォルダを2度見ないので、同名の譜面が複数の場所にあれば別の行として出る。
        /// </summary>
        public static List<DrillLevel> Find(string workspaceRoot, params string[] extraRoots)
        {
            return Find(true, workspaceRoot, extraRoots);
        }

        /// <param name="drillsOnly">
        /// false なら置き場所にある譜面を全部返す。名前で1本引き当てるときに使う
        /// （JumpDrill 以外の譜面のリプレイからでも、その譜面へ辿れるように）。
        /// </param>
        public static List<DrillLevel> Find(bool drillsOnly, string workspaceRoot, params string[] extraRoots)
        {
            var found = new List<DrillLevel>();

            foreach (var root in Roots(workspaceRoot, extraRoots))
            {
                string[] candidates;
                try { candidates = Directory.GetDirectories(root.Path); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                foreach (var candidate in candidates)
                {
                    // ドリルだけ要るなら、フォルダ名で先に落とす。
                    // フォルダ名は曲名から作る（SanitizeFolderName）ので、ドリルなら必ず
                    // "Drill" を含む。ここを通さないとカスタム曲2万件の Info.dat を
                    // 全部開くことになり、実測で 6.4 秒かかる。
                    if (drillsOnly && !LooksLikeDrillFolder(candidate)) continue;

                    string info = Path.Combine(candidate, "Info.dat");
                    if (!File.Exists(info)) continue;

                    string songName = ReadSongName(info);
                    if (songName == null) continue;
                    if (drillsOnly && !IsDrill(songName)) continue;

                    DateTime written;
                    try { written = File.GetLastWriteTime(info); }
                    catch (ArgumentException) { written = DateTime.MinValue; }

                    found.Add(new DrillLevel
                    {
                        Path = candidate,
                        SongName = songName,
                        Where = root.Label,
                        Written = written,
                    });
                }
            }

            found.Sort((a, b) => b.Written.CompareTo(a.Written));
            return found;
        }

        /// <summary>
        /// 曲名から譜面フォルダを逆引きする。見つからなければ null。
        ///
        /// フォルダ名は曲名から作っている（<see cref="LevelWriter.SanitizeFolderName"/>）ので、
        /// <b>まず名前で直接あるかどうかを見る</b>。置き場所ぶんの <c>Directory.Exists</c> で済む。
        ///
        /// 総当たりに落ちるのは、その場所に無かったときだけ。手でフォルダ名を変えた譜面も
        /// 拾えるようにするために残してある。カスタム曲が2万件あると
        /// 全件の <c>Info.dat</c> を開くことになり、1回 1.2 秒かかる。
        /// </summary>
        public static string FindByName(string songName, string workspaceRoot, params string[] extraRoots)
        {
            return FindByName(songName, true, workspaceRoot, extraRoots);
        }

        /// <param name="deepSearch">
        /// 名前で引けなかったときに総当たりまでやるか。
        ///
        /// <b>消した譜面では必ず空振りする。</b>それでもカスタム曲2万件を舐めるので、
        /// 1回 0.8 秒かかる。ボタンを押したときのように「見つからないと困る」場面では true、
        /// 一覧の集計のように「無ければ無いで済む」場面では false。
        /// </param>
        public static string FindByName(string songName, bool deepSearch, string workspaceRoot, params string[] extraRoots)
        {
            if (string.IsNullOrEmpty(songName)) return null;

            string folderName = LevelWriter.SanitizeFolderName(songName);

            foreach (var root in Roots(workspaceRoot, extraRoots))
            {
                string candidate = Path.Combine(root.Path, folderName);
                if (!Directory.Exists(candidate)) continue;

                // 名前が同じでも中身が別の譜面ということはある。曲名で確かめる。
                string info = Path.Combine(candidate, "Info.dat");
                if (File.Exists(info) && string.Equals(ReadSongName(info), songName, StringComparison.Ordinal))
                    return candidate;
            }

            if (!deepSearch) return null;

            // 手でフォルダ名を変えた譜面はここでしか拾えない。
            foreach (var level in Find(false, workspaceRoot, extraRoots))
                if (string.Equals(level.SongName, songName, StringComparison.Ordinal)) return level.Path;

            return null;
        }

        /// <summary>
        /// フォルダ名だけを見た、ドリルらしさの当たり。
        ///
        /// 中身は見ないので<b>当たりが緩い</b>（"Cendrillon" のような曲名も通る）。
        /// 通ったものは <see cref="IsDrill"/> で曲名を確かめるので、これで十分。
        /// 手でフォルダ名を変えた譜面だけは、ここで落ちて一覧に出なくなる。
        /// </summary>
        public static bool LooksLikeDrillFolder(string folderPath)
        {
            string name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return name != null && name.IndexOf("Drill", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>JumpDrill が作った譜面か。ID を外した先頭が <c>Drill </c> なら当たり。</summary>
        public static bool IsDrill(string songName)
        {
            return !string.IsNullOrEmpty(songName) &&
                DrillNaming.StripId(songName).StartsWith(DrillNaming.Prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Info.dat の <c>_songName</c> を読む。
        /// 完全な JSON パーサを持ち出すほどではない。値が1つ取れれば十分。
        /// </summary>
        public static string ReadSongName(string infoPath)
        {
            string text;
            try { text = File.ReadAllText(infoPath); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }

            const string Key = "\"_songName\"";

            int key = text.IndexOf(Key, StringComparison.Ordinal);
            if (key < 0) return null;

            int colon = text.IndexOf(':', key + Key.Length);
            if (colon < 0) return null;

            int open = text.IndexOf('"', colon + 1);
            if (open < 0) return null;

            int close = text.IndexOf('"', open + 1);
            if (close < 0) return null;

            return text.Substring(open + 1, close - open - 1);
        }

        private static void Add(List<DrillRoot> roots, string path, string label)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            foreach (var existing in roots)
                if (string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)) return;

            roots.Add(new DrillRoot { Path = path, Label = label });
        }
    }
}
