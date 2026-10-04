using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace JumpDrill.Output
{
    /// <summary>
    /// SongCore の <c>UserData\SongCore\folders.xml</c> を扱う。
    ///
    /// ここにフォルダを登録すると独立したレベルパックとして曲一覧に出せる。
    /// <c>WIP=True</c> にすれば WIP 扱いのまま（練習モードのみ・スコア送信なし）なので、
    /// 公開したくない自作ドリルをまとめて並べるのに使える。
    ///
    /// プレイリストではこれができない。WIP のレベルは levelID が
    /// <c>custom_level_&lt;HASH&gt; WIP</c> で登録されるのに対し、
    /// プレイリストはハッシュから <c>custom_level_&lt;HASH&gt;</c> を組み立てて引くため、
    /// 突き合わせに失敗する。
    /// </summary>
    public static class SongCoreFolders
    {
        /// <summary>folders.xml の1エントリ。</summary>
        public sealed class Entry
        {
            public string Name { get; internal set; }
            public string Path { get; internal set; }

            /// <summary>0=通常パックに混ぜる / 1=WIPパック / 2=独立パック。</summary>
            public int Pack { get; internal set; }

            public bool Wip { get; internal set; }

            /// <summary>パック一覧に出すカバー画像。未設定なら null。</summary>
            public string ImagePath { get; internal set; }

            /// <summary>雛形として置かれているダミー行か。</summary>
            public bool IsExample
            {
                get
                {
                    return string.Equals(Name, "Example", StringComparison.OrdinalIgnoreCase)
                        || !System.IO.Path.IsPathRooted(Path ?? string.Empty);
                }
            }
        }

        /// <summary>インストール先から folders.xml の場所を作る。</summary>
        public static string FoldersXmlFor(string installRoot)
        {
            if (string.IsNullOrEmpty(installRoot)) return null;
            return Path.Combine(installRoot, "UserData", "SongCore", "folders.xml");
        }

        /// <summary>登録済みのエントリ。読めなければ空。</summary>
        public static List<Entry> Read(string foldersXmlPath)
        {
            var result = new List<Entry>();
            if (string.IsNullOrEmpty(foldersXmlPath) || !File.Exists(foldersXmlPath)) return result;

            try
            {
                var doc = XDocument.Parse(File.ReadAllText(foldersXmlPath));
                if (doc.Root == null) return result;

                foreach (var element in doc.Root.Elements("folder"))
                {
                    int pack;
                    int.TryParse(Value(element, "Pack"), out pack);

                    result.Add(new Entry
                    {
                        Name = Value(element, "Name"),
                        Path = Value(element, "Path"),
                        Pack = pack,
                        Wip = string.Equals(Value(element, "WIP"), "True", StringComparison.OrdinalIgnoreCase),
                        ImagePath = Value(element, "ImagePath"),
                    });
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Xml.XmlException) { }

            return result;
        }

        private static string Value(XElement parent, string name)
        {
            var child = parent.Element(name);
            return child == null ? null : child.Value.Trim();
        }

        /// <summary>そのフォルダが既に登録されているか。</summary>
        public static bool IsRegistered(string foldersXmlPath, string folderPath)
        {
            foreach (var entry in Read(foldersXmlPath))
                if (SamePath(entry.Path, folderPath)) return true;
            return false;
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(
                a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 独立パックとして登録する。既に入っていれば false。
        ///
        /// XML を組み直すと元ファイルの注釈や整形が失われるので、
        /// 読み取りだけ XML で行い、書き込みは末尾への差し込みで済ませる。
        /// </summary>
        public static bool Register(string foldersXmlPath, string name, string folderPath, bool wip = true, bool withCover = true)
        {
            if (string.IsNullOrEmpty(foldersXmlPath)) throw new ArgumentException(Lang.T("folders.xml の場所が空です。", "The folders.xml path is empty."), nameof(foldersXmlPath));
            if (!File.Exists(foldersXmlPath)) throw new FileNotFoundException(Lang.T("folders.xml が見つかりません。", "folders.xml not found."), foldersXmlPath);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(Lang.T("パック名が空です。", "The pack name is empty."), nameof(name));
            if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException(Lang.T("フォルダが空です。", "The folder is empty."), nameof(folderPath));

            if (IsRegistered(foldersXmlPath, folderPath)) return false;

            Directory.CreateDirectory(folderPath);
            Backup(foldersXmlPath);

            // カバーはパックのフォルダ直下に置く。中の各フォルダは譜面として
            // 読まれるので、そこに混ぜずに1階層上に置いている。
            string imagePath = null;
            if (withCover)
            {
                imagePath = Path.Combine(folderPath, "pack-cover.png");
                if (!File.Exists(imagePath))
                    File.WriteAllBytes(imagePath, PackCoverRenderer.Render());
            }

            string raw = File.ReadAllText(foldersXmlPath);
            int close = raw.LastIndexOf("</folders>", StringComparison.OrdinalIgnoreCase);
            if (close < 0) throw new InvalidOperationException(Lang.T("folders.xml の書式が想定と違います（</folders> が見つかりません）。", "folders.xml has an unexpected format (</folders> not found)."));

            var entry = new StringBuilder();
            entry.Append("  <folder>\n");
            entry.Append("    <Name>").Append(Escape(name)).Append("</Name>\n");
            entry.Append("    <Path>").Append(Escape(folderPath)).Append("</Path>\n");
            entry.Append("    <Pack>2</Pack>\n");
            entry.Append("    <WIP>").Append(wip ? "True" : "False").Append("</WIP>\n");
            if (imagePath != null)
                entry.Append("    <ImagePath>").Append(Escape(imagePath)).Append("</ImagePath>\n");
            entry.Append("  </folder>\n");

            string updated = raw.Substring(0, close) + entry + raw.Substring(close);
            File.WriteAllText(foldersXmlPath, updated, new UTF8Encoding(false));
            return true;
        }

        /// <summary>登録結果1件。</summary>
        public sealed class RegisterResult
        {
            public string InstallName { get; internal set; }
            public string FoldersXmlPath { get; internal set; }

            /// <summary>今回追加したか。既に入っていれば false。</summary>
            public bool Added { get; internal set; }
        }

        /// <summary>
        /// 見つかった全ての Beat Saber インストールに同じフォルダを登録する。
        /// パックのフォルダはインストールの外に1つ置いて、各バージョンから
        /// 参照させるのが扱いやすい（BSManager はインスタンスを作り直すため）。
        /// </summary>
        public static List<RegisterResult> RegisterEverywhere(string name, string folderPath, bool wip = true, bool withCover = true)
        {
            var results = new List<RegisterResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in LevelWriter.FindLevelFolders())
            {
                if (string.IsNullOrEmpty(folder.InstallRoot)) continue;

                string xml = FoldersXmlFor(folder.InstallRoot);
                if (xml == null || !File.Exists(xml) || !seen.Add(xml)) continue;

                results.Add(new RegisterResult
                {
                    InstallName = folder.InstallName,
                    FoldersXmlPath = xml,
                    Added = Register(xml, name, folderPath, wip, withCover),
                });
            }

            return results;
        }

        /// <summary>書き換える前に1度だけ控えを取る。</summary>
        private static void Backup(string path)
        {
            string backup = path + ".bak-jumpdrill";
            if (!File.Exists(backup)) File.Copy(path, backup);
        }

        private static string Escape(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
