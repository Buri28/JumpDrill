using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Model;
using JumpDrill.Output;

namespace JumpDrill.Replays
{
    /// <summary>
    /// ドリルの記録を読んで、譜面ごとにまとめるための共通処理。
    /// 成績（ローカルリーダーボード）とメダルの両方が同じ読み方をする。
    /// </summary>
    public static class DrillRecords
    {
        /// <summary>
        /// JumpDrill が作った譜面のリプレイを読み、満点の分母を譜面のノーツ数に入れ直す。
        ///
        /// <b>ドリル以外は読まない。</b>再現性は「同じ2点の間を往復し続ける」前提で出しているので、
        /// 普通の曲は往復ずれが出せず満点扱いになり、叩けば叩くほど上位に並ぶ。
        /// 読み込みも重い（フレームを全部展開して解析する）。
        ///
        /// 再現性が出せなかったもの（振りが少なすぎる等）は落とす。
        /// ファイル名だけの絞り込みは曲名の一部に当たるので、曲名でも確かめる。
        /// </summary>
        /// <param name="installRoot">この Beat Saber の記録だけを読む。null なら見つかったもの全部。</param>
        public static List<ReplayScore> Load(string workspaceRoot, Action<string, Exception> onError = null, string installRoot = null)
        {
            var files = ReplayLibrary.FindReplayFiles(ReplayLibrary.DrillFilter, installRoot);
            var loaded = ReplayLibrary.LoadScores(files, onError);
            ApplyMapNotes(loaded, workspaceRoot);

            return loaded.Where(s => s.PerHand.Count > 0 &&
                DrillNaming.StripId(s.Replay.Info.SongName ?? "")
                    .StartsWith(ReplayLibrary.DrillFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// 満点の分母を<b>譜面のノーツ数</b>に入れ直す。
        ///
        /// リプレイからは数えられない。途中でやめると、そこから先のノーツはスポーンせず
        /// 記録にも残らない。実測でも「右30秒 → 左30秒」のドリルを右で切り上げた記録に、
        /// 左のノーツが Good も Miss も 0 件しか入っていなかった。
        /// そのままだと<b>片手しか振っていない記録がベストになる</b>。
        ///
        /// 譜面が見つからないもの（消した・移した）は、リプレイに出たぶんのままにする。
        /// </summary>
        public static void ApplyMapNotes(List<ReplayScore> scores, string workspaceRoot)
        {
            // 要るのは記録に出てくる曲名ぶんだけ。置き場所の譜面を全部並べると、
            // カスタム曲2万件の Info.dat を毎回開くことになる（実測 1.2 秒）。
            var counted = new Dictionary<string, MapNotes.Counts>(StringComparer.Ordinal);

            foreach (var score in scores)
            {
                string name = score.Replay.Info.SongName ?? "";

                MapNotes.Counts counts;
                if (!counted.TryGetValue(name, out counts))
                {
                    // 見つからなければ分母を入れ直さないだけ。総当たりまではしない。
                    string folder = DrillLibrary.FindByName(name, false, workspaceRoot);
                    if (folder == null || !MapNotes.TryCount(folder, out counts))
                        counts = new MapNotes.Counts();

                    counted.Add(name, counts);
                }

                if (counts.Total > 0) SwingAnalyzer.ApplyMapNotes(score, counts.Left, counts.Right);
            }
        }

        /// <summary>
        /// 譜面の ID。ドリルでなければ null。
        ///
        /// 新しい譜面は名前の頭に入っているのでそれを読むだけ。
        /// ID を入れる前に録った記録（<c>Drill … 100ms</c>）は、名前にある遷移と向きと、
        /// リプレイから出した片手の間隔で鍵を組み立てる（<c>LegacyId</c>）。
        /// 当時の名前は尺を持っていないので、いま作る譜面とは別の行になる。
        ///
        /// 旧い名前が持っているのはノーツ間隔で片手の間隔ではないが、
        /// <c>HandIntervalMs</c> は実測をその整数倍に丸めた値なので、そのまま使える。
        /// </summary>
        public static string MapIdOf(ReplayScore score)
        {
            string name = score.Replay.Info.SongName ?? "";

            string id = DrillNaming.ExtractId(name);
            if (id != null) return id;

            string body = DrillNaming.LegacyBody(name);
            if (body != null && score.HandIntervalMs > 0) return DrillNaming.LegacyId(body, score.HandIntervalMs);

            return null;
        }

        /// <summary>
        /// ベストは<b>再現精度 %</b> がいちばん高いもの。
        ///
        /// 再現性そのもので選んではいけない。あれは本数を見ないので、
        /// 少ししか振らずに崩れる前にやめた記録が上位に出る。
        ///
        /// 記録として数えるもの（<see cref="ReplayInfo.CountsAsRecord"/>）が 1 件でもあればそちらから選ぶ。
        /// 自動プレイはヘッドセットが動かないぶん再現性がほぼ満点になり、
        /// 速度を変えたプレイは別の速さを叩いている。どちらも数えるものが無いときだけ出す
        /// （その順で、速度を変えた人のプレイを自動プレイより先に）。
        /// </summary>
        public static ReplayScore PickBest(IEnumerable<ReplayScore> plays)
        {
            ReplayScore best = null;

            foreach (var play in plays)
            {
                if (best == null) { best = play; continue; }

                int rank = Rank(play), bestRank = Rank(best);
                if (rank != bestRank) { if (rank < bestRank) best = play; continue; }
                if (play.ReproducibilityPercent > best.ReproducibilityPercent) best = play;
            }

            return best;
        }

        /// <summary>ベストに選ぶ順。小さいほど先。</summary>
        private static int Rank(ReplayScore play)
        {
            var info = play.Replay.Info;
            if (info.CountsAsRecord) return 0;
            return info.LooksLikeAutoplay ? 2 : 1;
        }
    }
}
