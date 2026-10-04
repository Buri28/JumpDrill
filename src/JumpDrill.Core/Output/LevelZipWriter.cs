using System;
using System.IO;
using System.IO.Compression;

namespace JumpDrill.Output
{
    /// <summary>
    /// 譜面フォルダを BeatSaver 形式の zip に固める。
    ///
    /// BeatLeader のリプレイ表示は譜面を BeatSaver からハッシュで取りに行くので、
    /// 未公開の自作ドリルでは «Map was not found» になる。
    /// その画面から zip をローカルで読ませれば表示できるので、その受け渡し用。
    ///
    /// BeatSaver の配布物は Info.dat などを<b>zip 直下</b>に置く。
    /// フォルダごと固めて1階層深くすると読めないので、中身だけを入れる。
    /// </summary>
    public static class LevelZipWriter
    {
        /// <summary>既定の置き場所。ゲームのフォルダを汚さないよう、アプリの下に置く。</summary>
        public static string DefaultDirectory()
        {
            return Workspace.Default().ZipFolder;
        }

        /// <summary>
        /// <paramref name="levelFolder"/> の中身を zip にする。
        /// </summary>
        /// <returns>書き出した zip の場所。</returns>
        public static string Write(string levelFolder, string zipDirectory)
        {
            if (string.IsNullOrEmpty(levelFolder)) throw new ArgumentException(Lang.T("譜面フォルダが空です。", "The map folder is empty."), nameof(levelFolder));
            if (!Directory.Exists(levelFolder)) throw new DirectoryNotFoundException(Lang.T("譜面フォルダがありません: ", "Map folder not found: ") + levelFolder);
            if (string.IsNullOrEmpty(zipDirectory)) zipDirectory = DefaultDirectory();

            Directory.CreateDirectory(zipDirectory);

            string name = Path.GetFileName(levelFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string zipPath = Path.Combine(zipDirectory, name + ".zip");

            // 作り直しにする。追記だと前回の残骸が混ざる。
            if (File.Exists(zipPath)) File.Delete(zipPath);

            using (var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(levelFolder))
                {
                    var entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal);
                    using (var source = File.OpenRead(file))
                    using (var target = entry.Open())
                        source.CopyTo(target);
                }
            }

            return zipPath;
        }
    }
}
