using System;
using System.IO;
using System.Linq;
using JumpDrill.Output;
using UnityEngine;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 生成したドリルを置く専用パックを用意する。
    /// </summary>
    /// <remarks>
    /// <b>CustomLevels には置かない。</b> そこの譜面は普通にプレイでき、
    /// ScoreSaber / BeatLeader へスコアが送信される。自作の練習ドリルで
    /// 未ランクのリーダーボードを作ってしまう。
    ///
    /// 置き場所は SongCore の <c>folders.xml</c> に <c>Pack=2</c> / <c>WIP=True</c> で
    /// 登録した独立パック。JumpDrill の CLI の <c>--register-pack</c> と同じ形で、
    /// <list type="bullet">
    ///   <item>曲一覧に自分のパックとして1つにまとまる</item>
    ///   <item>WIP 扱いなのでスコアは送信されない（練習モードのみ）</item>
    /// </list>
    /// プレイリストではこれができない。WIP の levelID は
    /// <c>custom_level_&lt;HASH&gt; WIP</c> になるのに対し、プレイリストは
    /// ハッシュから <c>custom_level_&lt;HASH&gt;</c> を組み立てて引くため突き合わせに失敗する
    /// （JumpDrill.Core の <c>SongCoreFolders</c> の注釈）。
    /// </remarks>
    internal class DrillPackLocator
    {
        /// <summary>登録に失敗したときの逃げ場。ここも送信の対象外。</summary>
        private const string WipFolderName = "CustomWIPLevels";

        private string? cached;

        /// <summary>インストールのルート。</summary>
        /// <remarks>
        /// JumpDrill.Core の <c>LevelWriter.FindLevelFolders</c> は使わない。あれは機械にある
        /// Beat Saber を全部探して回るもので、ゲーム内では「今このゲーム」だけが要る。
        /// 探索にすると別のインスタンスに書いてしまう余地が残る。
        /// </remarks>
        private static string InstallRoot => InstallPaths.Root;

        /// <summary>
        /// 譜面の書き出し先。初回に folders.xml へ登録してから返す。
        /// </summary>
        /// <remarks>
        /// <b>直下に置く。</b> SongCore が譜面として読むのはパックのフォルダの
        /// 直下だけで、さらに1階層掘ると読まれない。
        /// </remarks>
        internal string ResolveOutputFolder(string packName)
        {
            if (cached != null) return cached;

            if (string.IsNullOrWhiteSpace(packName)) packName = "JumpDrill";

            string installRoot = InstallRoot;
            // パックのフォルダはインストールのルート直下。JumpDrill の CLI の --register-pack と同じ場所
            string folder = Path.Combine(installRoot, packName);
            string xml = SongCoreFolders.FoldersXmlFor(installRoot);

            try
            {
                if (SongCoreFolders.Register(xml, packName, folder, wip: true))
                    Plugin.LogDebug($"registered pack '{packName}' in {xml}");
                else
                    Plugin.LogDebug($"pack '{packName}' already registered");

                AddToSongCore(packName, folder);

                cached = folder;
                return folder;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                                      e is InvalidOperationException || e is ArgumentException)
            {
                // folders.xml が無い・書式が違う・書けない。譜面が作れないよりは、
                // 送信の対象外である CustomWIPLevels に落として動かす
                Plugin.Log?.Error($"could not register the pack, falling back to {WipFolderName}: {e}");

                string fallback = Path.Combine(InstallPaths.DataPath, WipFolderName);
                Directory.CreateDirectory(fallback);
                cached = fallback;
                return fallback;
            }
        }

        /// <summary>
        /// SongCore がまだこのフォルダを知らなければ、その場で足す。
        /// </summary>
        /// <remarks>
        /// SongCore は folders.xml を起動時にしか読まない。このゲームで初めてメニューに入った回に
        /// 登録すると、そのままでは再起動するまで一覧に出ず、生成しても「まだ生成されていない」になる。
        /// 足すのは folders.xml に書いたのと同じ形（独立パック・WIP）。次の起動からは folders.xml から読まれる。
        ///
        /// <b>読み込み中は足さない。</b> SongCore は読み込みの間、別スレッドで独立フォルダの一覧を順に見ている。
        /// そこへ足すと一覧の列挙が壊れ、そのセッションでは独立パックが丸ごと出なくなることがある。
        /// 読み込み中なら終わるのを待って足し、読み直させる。
        /// </remarks>
        private static void AddToSongCore(string packName, string folder)
        {
            try
            {
                var separates = SongCore.Loader.SeparateSongFolders;
                if (separates != null && separates.Any(f => SamePath(f.SongFolderEntry?.Path, folder))) return;

                if (SongCore.Loader.AreSongsLoading)
                {
                    Action<SongCore.Loader, System.Collections.Concurrent.ConcurrentDictionary<string, BeatmapLevel>>? handler = null;
                    handler = (loader, _) =>
                    {
                        SongCore.Loader.SongsLoadedEvent -= handler;
                        if (AddNow(packName, folder)) loader.RefreshSongs(false);
                    };
                    SongCore.Loader.SongsLoadedEvent += handler;
                    return;
                }

                AddNow(packName, folder);
            }
            catch (Exception e)
            {
                // 足せなくても、次の起動からは folders.xml から読まれる
                Plugin.Log?.Warn("could not add the pack to SongCore: " + e.Message);
            }
        }

        /// <summary>足す。足したら true（もう入っていた・失敗したら false）。</summary>
        private static bool AddNow(string packName, string folder)
        {
            try
            {
                var separates = SongCore.Loader.SeparateSongFolders;
                if (separates != null && separates.Any(f => SamePath(f.SongFolderEntry?.Path, folder))) return false;

                Directory.CreateDirectory(folder);
                SongCore.Collections.AddSeparateSongFolder(packName, folder, SongCore.Data.FolderLevelPack.NewPack,
                    LoadCover(Path.Combine(folder, "pack-cover.png")), wip: true);
                Plugin.LogDebug($"added pack '{packName}' to SongCore");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("could not add the pack to SongCore: " + e.Message);
                return false;
            }
        }

        /// <summary>パックの表紙。読めなければ null（SongCore の既定の絵になる）。表紙のせいでパックを諦めない。</summary>
        private static Sprite? LoadCover(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var texture = new Texture2D(2, 2);
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("could not load the pack cover: " + e.Message);
                return null;
            }
        }

        private static bool SamePath(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(
                Path.GetFullPath(a!).TrimEnd('\\', '/'),
                Path.GetFullPath(b!).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 登録済みかどうかにかかわらず、パックを用意しておく。
        /// </summary>
        /// <remarks>
        /// SongCore は folders.xml を起動時に読む。生成のときに初めて登録すると、
        /// <b>その回は曲一覧に出ない。</b> メニューに入った時点で通しておけば、
        /// 少なくとも初回起動の1回で済む。
        /// </remarks>
        internal void Prepare(string packName) => ResolveOutputFolder(packName);
    }
}
