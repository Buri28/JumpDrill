using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Replays;
using JumpDrillMod.Models;

namespace JumpDrillMod.Services
{
    /// <summary>
    /// 保存済みのリプレイを読んで、ドリルの記録を並べる。
    /// </summary>
    /// <remarks>
    /// <b>記録はリプレイから出す。</b> BeatLeader と LocalLeaderboard が
    /// 練習モードのプレイもリプレイに残していて、JumpDrill の GUI のスコアボードは
    /// それを読んでいる。同じものを読んで同じ解析にかければ、両方が一致する。
    ///
    /// BeatLeader が無い環境のために、このMODも同じ形式のリプレイを残す
    /// （<see cref="Gameplay.DrillReplayRecorder"/>）。BeatLeader も同じプレイを残していれば
    /// そちらを採る（JumpDrill.Core の <c>ReplayLibrary.RemoveDuplicates</c>）。
    ///
    /// 解析は JumpDrill.Core（<c>SwingAnalyzer</c>）。このMODは読む場所を決めて渡すだけ。
    ///
    /// リーダーボード（1譜面ぶん）とメダル（全ドリルぶん）の両方がここを使う。
    /// 解析結果は<b>ファイル単位</b>で持つので、メダル画面で読んだものは
    /// リーダーボードでも読み直さずに済む。
    /// </remarks>
    internal class ReplayScoreReader
    {
        private readonly DrillPackLocator pack;
        private readonly LevelLauncher levels;

        /// <summary>
        /// ドリルの ID → 譜面のフォルダ。SongCore の一覧はメインスレッドでしか引けないので、
        /// メインスレッドで控えておき、別スレッドの解析はこれを見る。丸ごと差し替えるだけで書き換えない。
        /// </summary>
        private volatile Dictionary<string, string> levelFolders =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>専用パックのフォルダ。<see cref="levelFolders"/> に無いときの探し先。</summary>
        private volatile string? packFolder;

        /// <summary>
        /// ファイルごとの解析結果。更新時刻が変わっていたら読み直す。
        /// 読めなかったものは null で持ち、同じ壊れたファイルを何度も開かない。
        /// </summary>
        private readonly Dictionary<string, Cached> cache =
            new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 解析はメダル画面の読み込み（別スレッド）と、リーダーボード（メインスレッド）の
        /// 両方から走る。<see cref="cache"/> を同時に触らせない。
        /// </summary>
        private readonly object gate = new object();

        private sealed class Cached
        {
            public DateTime WrittenUtc;
            public DrillScore? Score;

            /// <summary>ノーツ数を数えた譜面のフォルダ。見つからなかったら null。</summary>
            public string? MapFolder;
        }

        internal ReplayScoreReader(DrillPackLocator pack, LevelLauncher levels)
        {
            this.pack = pack;
            this.levels = levels;
        }

        /// <summary>
        /// 譜面のフォルダの控えを今の曲一覧で作り直す。メインスレッドで呼ぶこと。
        /// </summary>
        internal void RefreshLevelFolders(string packName)
        {
            try
            {
                packFolder = pack.ResolveOutputFolder(packName);

                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var level in levels.DrillLevels())
                    if (!string.IsNullOrEmpty(level.Folder)) map[level.Id] = level.Folder;
                levelFolders = map;
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn("could not list the drill folders: " + e.Message);
            }
        }

        /// <summary>そのドリルの記録を、再現精度の高い順に。</summary>
        internal IReadOnlyList<DrillScore> For(string? drillId, string packName)
        {
            if (string.IsNullOrEmpty(drillId)) return new List<DrillScore>();

            RefreshLevelFolders(packName);

            try
            {
                string token = "[" + drillId + "]";

                return ListReplayFiles()
                    .Where(file => Path.GetFileName(file).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(file => Analyze(file))
                    .Where(score => score != null)
                    .Select(score => score!)
                    // 記録として数えるものを先に。自動プレイと速度を変えたものは比べる相手にならない（GUI と同じ）
                    .OrderBy(s => !s.CountsAsRecord)
                    .ThenByDescending(s => s.ReproducibilityPercent)
                    .ThenByDescending(s => s.PlayedAt)
                    .ToList();
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not read the replays: " + e);
                return new List<DrillScore>();
            }
        }

        /// <summary>
        /// ID ごとの、人のプレイのベスト再現精度 %。メダルを決める値。
        /// </summary>
        /// <remarks>
        /// JumpDrill.Core の <c>DrillSetMedals.BestById</c> と同じ決め方（自動プレイと、速度を変えたものは入れない）。
        /// あちらは <c>ReplayScore</c> の一覧を受けるので、ここでは同じ規則で数字だけ集める。
        ///
        /// フォルダの一覧は<b>1回だけ</b>取る。ドリルは 112 本あり、ID ごとに走査すると
        /// 在庫の多い環境で待たされる。
        /// </remarks>
        /// <remarks>
        /// 別スレッドから呼んでよい。先にメインスレッドで <see cref="RefreshLevelFolders"/> を呼んでおくこと。
        /// </remarks>
        internal Dictionary<string, double> BestById()
        {
            var best = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var file in ListReplayFiles())
            {
                // ドリル以外は開かない。解析が重いうえ、再現性は往復が前提の数字なので
                // 普通の曲では意味を持たない（JumpDrill.Core の DrillRecords.Load と同じ理由）
                if (Path.GetFileName(file).IndexOf(ReplayLibrary.DrillFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var score = Analyze(file);
                if (score == null || !score.CountsAsRecord) continue;

                string? id = DrillNaming.ExtractId(score.SongName);
                if (id == null) continue;

                double current;
                if (!best.TryGetValue(id, out current) || score.ReproducibilityPercent > current)
                    best[id] = score.ReproducibilityPercent;
            }

            return best;
        }

        /// <summary>
        /// 今このゲームのインストールにあるリプレイを全部。
        /// </summary>
        /// <remarks>
        /// 探すのは<b>今このゲームのインストールの下だけ</b>。JumpDrill.Core の
        /// <c>ReplayLibrary.FindReplayFolders</c> は機械にある Beat Saber を全部見るが、
        /// ゲーム内では他のインスタンスの記録まで混ぜる意味が無い。
        ///
        /// 同じプレイが複数のMODに残っていれば、JumpDrill.Core と同じ決め方
        /// （<c>ReplayLibrary.RemoveDuplicates</c>）で1つにする。
        /// </remarks>
        private static List<string> ListReplayFiles()
        {
            string userData = InstallPaths.UserData;
            var all = new List<string>();

            foreach (var mod in ReplayLibrary.ReplayMods)
            {
                string folder = Path.Combine(Path.Combine(userData, mod), "Replays");
                if (!Directory.Exists(folder)) continue;

                try { all.AddRange(Directory.GetFiles(folder, "*.bsor")); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            return ReplayLibrary.RemoveDuplicates(all);
        }

        /// <summary>
        /// 1ファイルを解析する。前に読んでいて変わっていなければそれを返す。
        /// </summary>
        /// <remarks>
        /// ノーツ数を数える譜面が前と変わったとき（前は無かった譜面を後から作ったなど）も読み直す。
        /// 分母が変わるので数字も変わる。
        /// </remarks>
        private DrillScore? Analyze(string file)
        {
            DateTime written;
            try { written = File.GetLastWriteTimeUtc(file); }
            catch (IOException) { return null; }

            lock (gate)
            {
                Cached found;
                if (cache.TryGetValue(file, out found) && found.WrittenUtc == written)
                {
                    if (found.Score == null) return null;
                    if (string.Equals(MapFolderFor(found.Score.SongName), found.MapFolder, StringComparison.OrdinalIgnoreCase))
                        return found.Score;
                }
            }

            DrillScore? score = null;
            string? mapFolder = null;
            try
            {
                var replay = ReplayReader.Read(file);
                var analyzed = SwingAnalyzer.Analyze(replay);

                // 振りが少なすぎて再現性が出せなかったものは落とす（JumpDrill.Coreと同じ）
                if (analyzed.PerHand.Count > 0)
                {
                    mapFolder = ApplyMapNotes(analyzed, replay.Info.SongName);
                    score = ToRecord(analyzed, file, replay);
                }
            }
            catch (Exception e)
            {
                // 1件読めなくても他は出す
                Plugin.Log?.Warn($"could not read {Path.GetFileName(file)}: {e.Message}");
            }

            lock (gate)
                cache[file] = new Cached { WrittenUtc = written, Score = score, MapFolder = mapFolder };

            return score;
        }

        /// <summary>
        /// 満点の分母を譜面のノーツ数で入れ直す。
        /// </summary>
        /// <remarks>
        /// リプレイからは数えられない。途中でやめると、そこから先のノーツは
        /// スポーンせず記録にも残らないので、片手ぶん丸ごと抜けることがある
        /// （JumpDrill.Core の <c>MapNotes</c>）。譜面が見つからなければ入れ直さないだけにする。
        /// </remarks>
        /// <returns>見た譜面のフォルダ。見つからなければ null。</returns>
        private string? ApplyMapNotes(ReplayScore score, string? songName)
        {
            string? folder = MapFolderFor(songName);
            if (folder == null) return null;

            // 数えられなくても見たフォルダを返す。null を返すと、次に読むたびに
            // 「譜面が変わった」とみなして解析をやり直してしまう
            // 1.44 の SongCore は、許可なしに譜面を直接読むと例外にする（SongCoreFileAccess）。
            // 読めなくても記録は出す（満点の分母を入れ直さないだけ）
            try
            {
                MapNotes.Counts counts = default;
                bool counted = SongCoreFileAccess.Run(() => MapNotes.TryCount(folder, out counts));
                if (counted && counts.Total > 0)
                    SwingAnalyzer.ApplyMapNotes(score, counts.Left, counts.Right);
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn($"could not count the notes in {folder}: {e.Message}");
            }
            return folder;
        }

        /// <summary>
        /// その曲名の譜面のフォルダ。読み込まれている譜面を ID で引き、無ければ専用パックの中を
        /// 曲名のフォルダ名で探す（まだ曲一覧に読まれていない書きたての譜面のため）。
        /// </summary>
        private string? MapFolderFor(string? songName)
        {
            if (string.IsNullOrEmpty(songName)) return null;

            string? id = DrillNaming.ExtractId(songName);
            string found;
            if (id != null && levelFolders.TryGetValue(id, out found) && Directory.Exists(found)) return found;

            string? root = packFolder;
            if (root == null) return null;

            string guess = Path.Combine(root, LevelWriter.SanitizeFolderName(songName!));
            return Directory.Exists(guess) ? guess : null;
        }

        /// <summary>手ごとの成績。その手の振りが無ければ null。</summary>
        private static SwingScore? Hand(ReplayScore score, int saberType)
        {
            return score.PerHand.FirstOrDefault(h => h.SaberType == saberType);
        }

        /// <summary>正面図に添える 再現・点数・角度。その手の振りが無ければ null。</summary>
        private static HandFigures? FiguresOf(SwingScore? hand)
        {
            if (hand == null) return null;

            return new HandFigures
            {
                Reproducibility = hand.Score,
                AverageCut = hand.AverageCut,
                AnglePercent = hand.AnglePercent,
            };
        }

        /// <summary>横から見た図に添える PRE / POST。その向きの振りが無ければ null。</summary>
        private static SwingAngles? AnglesOf(DirectionScore? side)
        {
            if (side == null) return null;

            return new SwingAngles { PreSwing = side.PreSwing, PostSwing = side.PostSwing };
        }

        /// <remarks>日時はリプレイの更新時刻で見る。JumpDrill の GUI と同じ。</remarks>
        private static DrillScore ToRecord(ReplayScore score, string file, Replay replay)
        {
            return new DrillScore
            {
                SongName = replay.Info.SongName ?? string.Empty,
                ReplayPath = file,
                PlayedAt = File.GetLastWriteTime(file),
                ReproducibilityPercent = score.ReproducibilityPercent,
                LeftReproducibilityPercent = Hand(score, 0)?.ReproducibilityPercent,
                RightReproducibilityPercent = Hand(score, 1)?.ReproducibilityPercent,
                LeftAccuracy = Hand(score, 0)?.Accuracy,
                RightAccuracy = Hand(score, 1)?.Accuracy,
                LeftMissCount = Hand(score, 0)?.MissCount,
                RightMissCount = Hand(score, 1)?.MissCount,
                LeftHand = FiguresOf(Hand(score, 0)),
                RightHand = FiguresOf(Hand(score, 1)),
                LeftFore = AnglesOf(Hand(score, 0)?.Fore),
                LeftBack = AnglesOf(Hand(score, 0)?.Back),
                RightFore = AnglesOf(Hand(score, 1)?.Fore),
                RightBack = AnglesOf(Hand(score, 1)?.Back),
                Reproducibility = score.Score,
                Accuracy = score.Accuracy,
                SwingCount = score.SwingCount,
                SwingableCount = score.SwingableCount,
                MissCount = score.MissCount,
                DurationSeconds = score.DurationSeconds,
                ModifiedPractice = replay.Info.HasPracticeSettings,
                Autoplay = replay.Info.LooksLikeAutoplay,
                SpeedChanged = replay.Info.SongSpeedChanged,
                CountsAsRecord = replay.Info.CountsAsRecord,
            };
        }
    }
}
