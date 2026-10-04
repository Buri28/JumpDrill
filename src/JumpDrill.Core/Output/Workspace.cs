using System;
using System.IO;

namespace JumpDrill.Output
{
    /// <summary>
    /// 生成物の置き場所。
    ///
    /// パックのフォルダは Beat Saber のインストール先の中に置く必要はない。
    /// SongCore は folders.xml の Path をそのまま使うだけで、場所を問わない
    /// （<c>Collections::AddSeparateSongFolder</c> は存在しなければ作るだけ）。
    /// むしろ BSManager はバージョンごとにインスタンスを作り直すので、
    /// 中に置くと消える。外に1か所持って、各インストールから参照させる。
    /// </summary>
    public sealed class Workspace
    {
        public Workspace(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException(Lang.T("作業フォルダが空です。", "The workspace folder is empty."), nameof(root));
            Root = Path.GetFullPath(root);
        }

        public string Root { get; }

        /// <summary>
        /// 既定はアプリの下の <c>workspace</c>。
        /// ユーザーフォルダに散らすより、ツールと生成物が一緒に置かれている方が追いやすい。
        /// exe は bin\... の奥にあるので、JumpDrill.sln のある階層まで遡って探す。
        /// </summary>
        public static string DefaultRoot()
        {
            return Path.Combine(AppRoot(), "workspace");
        }

        /// <summary>ツールの置かれている場所。</summary>
        public static string AppRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;

            var dir = new DirectoryInfo(start);
            for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "JumpDrill.sln")) ||
                    File.Exists(Path.Combine(dir.FullName, "drill-gui.bat")))
                    return dir.FullName;
            }

            return start.TrimEnd(Path.DirectorySeparatorChar);
        }

        public static Workspace Default()
        {
            return new Workspace(DefaultRoot());
        }

        /// <summary>SongCore に登録するパックのフォルダ。</summary>
        public string PackFolder(string packName)
        {
            if (string.IsNullOrWhiteSpace(packName)) throw new ArgumentException(Lang.T("パック名が空です。", "The pack name is empty."), nameof(packName));
            return Path.Combine(Root, "packs", packName);
        }

        /// <summary>リプレイ表示用の zip の置き場所。</summary>
        public string ZipFolder
        {
            get { return Path.Combine(Root, "zip"); }
        }

        /// <summary>パックを使わないときの既定の書き出し先。</summary>
        public string OutFolder
        {
            get { return Path.Combine(Root, "out"); }
        }
    }
}
