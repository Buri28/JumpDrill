using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using IPA.Loader;
using IPA.Utilities.Async;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 記録のリプレイを BeatLeader のゲーム内リプレイヤーで再生する。
    /// </summary>
    /// <remarks>
    /// <b>BeatLeader は必須にしない。</b> 記録は LocalLeaderboard のリプレイからも読めるので、
    /// BeatLeader が入っていなくてもリーダーボードは動く。再生だけができない。
    ///
    /// BeatLeader はビルド時に参照せず、型もメソッドも実行時に名前で探して呼ぶ。
    /// 引数の形が BeatLeader の版で変わる（0.10 で対戦用の引数が増え、結果も <c>Task&lt;bool&gt;</c> になった）うえ、
    /// 同じゲームの版でも入っている BeatLeader の版はそろわない。ビルドした版の形で直接呼ぶと、
    /// 別の版では呼び出しごと読み込めずに落ちる。
    ///
    /// 使うのは BeatLeader の公開の口（<c>ReplayDecoder</c> と <c>ReplayerMenuLoader.StartReplayAsync</c>）。
    /// 譜面を探して場面を切り替え、終わったらメニューに戻すところまで BeatLeader がやる。
    /// LocalLeaderboard のリプレイも同じ BSOR 形式なので同じように渡せる。
    /// </remarks>
    internal static class BeatLeaderReplayer
    {
        private const string LoaderType = "BeatLeader.Replayer.ReplayerMenuLoader";
        private const string DecoderType = "BeatLeader.Models.Replay.ReplayDecoder";

        /// <summary>BeatLeader が読み込まれているか。無ければ再生のボタンを出さない。</summary>
        internal static bool IsAvailable => PluginManager.GetPluginFromId("BeatLeader") != null;

        /// <summary>
        /// 再生を始める。失敗したら理由を <paramref name="onError"/> で返す（メインスレッドで）。
        /// </summary>
        internal static void Play(string? replayPath, Action<string> onError)
        {
            var assembly = PluginManager.GetPluginFromId("BeatLeader")?.Assembly;
            if (assembly == null)
            {
                onError("BeatLeader is not installed.");
                return;
            }

            if (string.IsNullOrEmpty(replayPath) || !File.Exists(replayPath))
            {
                onError("The replay file is gone.");
                return;
            }

            try
            {
                Start(assembly, replayPath!, onError);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not start the replay: " + (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e));
                onError("Could not start the replay. See the log.");
            }
        }

        private static void Start(Assembly beatLeader, string replayPath, Action<string> onError)
        {
            var loaderType = beatLeader.GetType(LoaderType);
            var decoderType = beatLeader.GetType(DecoderType);
            if (loaderType == null || decoderType == null)
            {
                Plugin.Log?.Error("BeatLeader has no " + (loaderType == null ? LoaderType : DecoderType));
                onError("This BeatLeader version is not supported.");
                return;
            }

            var loader = loaderType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (loader == null)
            {
                onError("The BeatLeader replayer is not ready.");
                return;
            }

            var replay = Decode(decoderType, File.ReadAllBytes(replayPath));
            if (replay == null)
            {
                onError("Could not read the replay.");
                return;
            }

            // 0.9 の BeatLeader は譜面が見つからなくても知らせずに進むので、先にこちらで確かめる
            if (!LevelExists(replay))
            {
                onError("BeatLeader could not open this drill. Is it still in the pack?");
                return;
            }

            var start = StartReplayAsync(loader, replay);
            if (start == null)
            {
                onError("This BeatLeader version is not supported.");
                return;
            }

            // 譜面が見つからない（消した・パックが読まれていない）ときは false が返る。
            // 0.9 の BeatLeader は結果を返さない（Task）ので、そのときは見ない
            start.ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Plugin.Log?.Error("replay failed: " + task.Exception);
                    onError("Could not start the replay. See the log.");
                }
                else if (task is Task<bool> found && !found.Result)
                {
                    onError("BeatLeader could not open this drill. Is it still in the pack?");
                }
            }, UnityMainThreadTaskScheduler.Default);
        }

        /// <summary>
        /// リプレイの譜面（<c>info.hash</c>）が読み込まれているか。曲の読み込み中や、
        /// hash が読めないときは確かめようがないので true にして BeatLeader に任せる。
        /// </summary>
        private static bool LevelExists(object replay)
        {
            if (SongCore.Loader.AreSongsLoading) return true;

            var info = replay.GetType().GetField("info")?.GetValue(replay);
            var hash = info?.GetType().GetField("hash")?.GetValue(info) as string;
            if (string.IsNullOrEmpty(hash)) return true;

            // levelID は "custom_level_" + hash。WIP なら後ろに " WIP"、同じ譜面が2か所にあれば
            // "_<フォルダ名>" が付き、どちらも hash に入っている。大文字にはしない（フォルダ名が変わる）
            string levelId = "custom_level_" + hash;
            if (SongCore.Loader.GetLevelById(levelId) != null) return true;

            // GetLevelById は CustomWIPLevels と CachedWIPLevels の曲を持っていないので、並びから探す
            return HasLevel(SongCore.Loader.CustomWIPLevels.Values, levelId)
                || HasLevel(SongCore.Loader.CachedWIPLevels.Values, levelId)
                || HasLevel(SongCore.Loader.CustomLevels.Values, levelId)
                || SongCore.Loader.SeparateSongFolders.Any(f => HasLevel(f.Levels.Values, levelId));
        }

        private static bool HasLevel(System.Collections.Generic.IEnumerable<BeatmapLevel> levels, string levelId)
        {
            return levels.Any(l => string.Equals(l?.levelID, levelId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// BSOR を BeatLeader のリプレイにする。<c>TryDecodeReplay(byte[], out Replay)</c> が無ければ
        /// <c>DecodeReplay(byte[])</c> を使う。読めなければ null。
        /// </summary>
        private static object? Decode(Type decoderType, byte[] bytes)
        {
            var methods = decoderType.GetMethods(BindingFlags.Public | BindingFlags.Static);

            var tryDecode = methods.FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return m.Name == "TryDecodeReplay" && m.ReturnType == typeof(bool) && ps.Length == 2 &&
                       ps[0].ParameterType == typeof(byte[]) && ps[1].ParameterType.IsByRef;
            });
            if (tryDecode != null)
            {
                var args = new object?[] { bytes, null };
                return (bool)tryDecode.Invoke(null, args) ? args[1] : null;
            }

            var decode = methods.FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return m.Name == "DecodeReplay" && ps.Length == 1 && ps[0].ParameterType == typeof(byte[]);
            });
            return decode?.Invoke(null, new object[] { bytes });
        }

        /// <summary>
        /// <c>ReplayerMenuLoader.StartReplayAsync</c> を、リプレイ以外は既定値で呼ぶ。
        /// </summary>
        /// <remarks>
        /// 最初の引数がリプレイのものから選ぶ。リプレイだけ渡せば済む形（ほかは省略できる）を先に、
        /// 無ければ必須の引数が少ない形を選び、リプレイ以外は既定値（参照型は null）で埋める。
        /// プレイヤー・設定・終わったときの呼び出しはどれも null で BeatLeader が既定のものを使う。
        /// </remarks>
        private static Task? StartReplayAsync(object loader, object replay)
        {
            var method = loader.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m =>
                {
                    if (m.Name != "StartReplayAsync" || !typeof(Task).IsAssignableFrom(m.ReturnType)) return false;
                    var ps = m.GetParameters();
                    return ps.Length > 0 && ps[0].ParameterType.IsInstanceOfType(replay);
                })
                .OrderBy(m => m.GetParameters().Skip(1).Count(p => !p.IsOptional))
                .ThenBy(m => m.GetParameters().Length)
                .FirstOrDefault();
            if (method == null)
            {
                Plugin.Log?.Error("BeatLeader has no StartReplayAsync that takes a replay");
                return null;
            }

            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = replay;
            for (int i = 1; i < parameters.Length; i++)
                args[i] = DefaultFor(parameters[i]);
            return method.Invoke(loader, args) as Task;
        }

        private static object? DefaultFor(ParameterInfo p)
        {
            if (p.HasDefaultValue && p.DefaultValue != null && p.DefaultValue != DBNull.Value) return p.DefaultValue;
            return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }
    }
}
