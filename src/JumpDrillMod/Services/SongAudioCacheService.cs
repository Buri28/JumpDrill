using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JumpDrill.Output;
using UnityEngine.SceneManagement;
using Zenject;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// ドリルの音を書き換えたら、ゲームが覚えている前の音を新しい音に差し替える。
    /// </summary>
    /// <remarks>
    /// ゲーム（<c>AudioClipAsyncLoader</c>）は音をファイルのパスごとに参照の数を数えて覚えている。
    /// 同じ譜面を続けて叩くと数が残り、曲一覧を全部読み直しても前の音が捨てられない。
    /// そのため上書きした後に叩くと、譜面は新しいのに音は前のまま（クリック音を変えても鳴る音が変わらない）になる。
    ///
    /// 曲一覧を読み直すたびに、ドリルの音のファイルが前に見たときから書き換わっていれば、
    /// 覚えている中身だけを読み直した音に替えて前の音を捨てる。参照の数には触らない
    /// （曲一覧のプレビューなど、参照を持っている側が後で返しに来ても数が合うように）。
    /// プレイ中は鳴っている音を捨てないよう、メニューに戻るまで待つ。
    /// メダル画面・Mods タブ・作り直し・削除のどこで書いても、GUI で書いて読み直しても効く。
    /// </remarks>
    internal sealed class SongAudioCacheService : IInitializable, IDisposable
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly LevelLauncher launcher;
        private readonly BeatmapLevelsModel? levelsModel;

        /// <summary>前に読み直したときのドリルの音のファイル。まだ見ていなければ null。</summary>
        private Dictionary<string, string>? seen;

        /// <summary>書き換わったのに、プレイ中でまだ差し替えていない音のパス。</summary>
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);

        private bool warned;

        internal SongAudioCacheService(LevelLauncher launcher, [InjectOptional] BeatmapLevelsModel? levelsModel)
        {
            this.launcher = launcher;
            this.levelsModel = levelsModel;
        }

        public void Initialize()
        {
            SongCore.Loader.SongsLoadedEvent += OnSongsLoaded;
            SceneManager.activeSceneChanged += OnSceneChanged;
            if (SongCore.Loader.AreSongsLoaded && !SongCore.Loader.AreSongsLoading) seen = Take();
        }

        public void Dispose()
        {
            SongCore.Loader.SongsLoadedEvent -= OnSongsLoaded;
            SceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private void OnSongsLoaded(SongCore.Loader loader, ConcurrentDictionary<string, BeatmapLevel> levels)
        {
            try
            {
                var now = Take();
                foreach (var path in SongFileSnapshot.Changed(seen!, now)) pending.Add(path);
                seen = now;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not check the drill audio: " + e);
            }
            Flush();
        }

        private void OnSceneChanged(Scene from, Scene to) => Flush();

        /// <summary>メニューにいれば、待っている音を差し替える。</summary>
        private void Flush()
        {
            if (pending.Count == 0 || SceneManager.GetActiveScene().name == "GameCore") return;

            foreach (var path in pending.ToList())
            {
                pending.Remove(path);
                try { Replace(path); }
                catch (Exception e) { Plugin.Log?.Error($"could not drop the old audio of {path}: {e}"); }
            }
        }

        private Dictionary<string, string> Take() => SongFileSnapshot.Take(launcher.DrillFolders());

        /// <summary>
        /// そのファイルの音をゲームが覚えていれば、中身を読み直した音に替え、前の音を捨てる。
        /// ゲームの鍵はパスの文字列なので、書き方の違う候補（フルパスなど）も見る。
        /// </summary>
        private void Replace(string path)
        {
            var audio = AudioLoader();
            if (audio == null) return;

            var cache = FieldOf(audio, "IReferenceCountingCache")?.GetValue(audio);
            var items = cache == null ? null : FieldOf(cache, "Dictionary", "_items")?.GetValue(cache) as IDictionary;
            var media = FieldOf(audio, "IMediaAsyncLoader")?.GetValue(audio);
            var load = media?.GetType().GetMethod("LoadAudioClipFromFilePathAsync", Fields, null, new[] { typeof(string) }, null);
            if (items == null || media == null || load == null)
            {
                WarnOnce("the audio cache of this game version is not supported");
                return;
            }

            foreach (var key in Candidates(path))
            {
                int hash = key.GetHashCode();
                if (!items.Contains(hash)) continue;

                var old = items[hash] as Task;
                items[hash] = load.Invoke(media, new object[] { key });
                if (old != null && old.Status == TaskStatus.RanToCompletion &&
                    old.GetType().GetProperty("Result")?.GetValue(old) is UnityEngine.Object clip && clip != null)
                    UnityEngine.Object.Destroy(clip);
                Plugin.LogDebug("replaced the old audio of " + key);
            }
        }

        private static IEnumerable<string> Candidates(string path)
        {
            var list = new List<string> { path };
            try
            {
                string folder = Path.GetDirectoryName(path) ?? string.Empty;
                string name = Path.GetFileName(path);
                list.Add(Path.Combine(Path.GetFullPath(folder), name));
                list.Add(Path.Combine(new DirectoryInfo(folder).FullName.TrimEnd('\\', '/'), name));
            }
            catch (Exception) { }
            return list.Distinct(StringComparer.Ordinal);
        }

        /// <summary>曲を読むときにゲームが使う <c>AudioClipAsyncLoader</c>。見つからなければ null。</summary>
        private object? AudioLoader()
        {
            var model = levelsModel ?? FieldOf(SongCore.Loader.Instance, "BeatmapLevelsModel")?.GetValue(SongCore.Loader.Instance) as BeatmapLevelsModel;
            var loader = model?.levelLoader;
            var audio = loader == null ? null : FieldOf(loader, "AudioClipAsyncLoader")?.GetValue(loader);
            if (audio == null) WarnOnce("could not find the audio loader");
            return audio;
        }

        /// <summary>
        /// 型の名前（ジェネリックは前の部分）で探すフィールド。版で名前が変わっても見つけられるように。
        /// 同じ型が複数あるときは <paramref name="name"/> で選ぶ。
        /// </summary>
        private static FieldInfo? FieldOf(object? owner, string typeName, string? name = null)
        {
            for (var type = owner?.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetFields(Fields | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => f.FieldType.Name.Split('`')[0] == typeName && (name == null || f.Name == name));
                if (field != null) return field;
            }
            return null;
        }

        private void WarnOnce(string message)
        {
            if (warned) return;
            warned = true;
            Plugin.Log?.Warn(message + "; an overwritten drill may play its old audio until the game restarts");
        }
    }
}
