using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Model;

namespace JumpDrill.Replays
{
    /// <summary>一括生成のドリル1本で取れたメダル。</summary>
    public enum Medal
    {
        None = 0,
        Bronze = 1,
        Silver = 2,
        Gold = 3,
    }

    /// <summary>1本ぶんの成績。</summary>
    public sealed class MedalCell
    {
        public DrillSetEntry Entry { get; internal set; }

        /// <summary>譜面の ID。記録はこれで引く。</summary>
        public string Id { get; internal set; }

        /// <summary>人のプレイのベスト再現精度 %。叩いていなければ null。</summary>
        public double? BestPercent { get; internal set; }

        public Medal Medal { get; internal set; }
    }

    /// <summary>1方向ぶんの成績。</summary>
    public sealed class MedalRow
    {
        /// <summary>方向の名前（<c>R3bL2a</c>）。</summary>
        public string Direction { get; internal set; }

        /// <summary>Stage の並び（<see cref="DrillSet.Entries"/> と同じ順）。</summary>
        public List<MedalCell> Cells { get; internal set; }

        /// <summary>この方向の Lv。メダルのポイントの合計。</summary>
        public int Level { get { return Cells.Sum(c => DrillSetMedals.Points(c.Medal)); } }
    }

    /// <summary>
    /// 一括生成のドリルのメダルと Lv。
    ///
    /// メダルは<b>再現精度 %</b> で決める（🥉 70 / 🥈 80 / 🥇 90 以上）。
    /// 成績の一覧で順位に使っているのと同じ数字で、ミスで振れなかったぶんが差し引かれているので
    /// ミス数の条件は別に要らない。
    ///
    /// Lv はメダルのポイント（🥉1 🥈2 🥇3）の合計。どの Stage から取ってもよい。
    ///
    /// <b>自動プレイは数えない。</b>ヘッドセットが動かないぶん再現性がほぼ満点になる。
    /// </summary>
    public static class DrillSetMedals
    {
        public const double BronzePercent = 70.0;
        public const double SilverPercent = 80.0;
        public const double GoldPercent = 90.0;

        /// <summary>再現精度 % から取れるメダル。</summary>
        public static Medal MedalOf(double reproducibilityPercent)
        {
            if (reproducibilityPercent >= GoldPercent) return Medal.Gold;
            if (reproducibilityPercent >= SilverPercent) return Medal.Silver;
            if (reproducibilityPercent >= BronzePercent) return Medal.Bronze;
            return Medal.None;
        }

        /// <summary>メダルのポイント。🥉1 🥈2 🥇3。</summary>
        public static int Points(Medal medal)
        {
            return (int)medal;
        }

        /// <summary>表示用の絵文字。取れていなければ「・」。</summary>
        public static string Emoji(Medal medal)
        {
            switch (medal)
            {
                case Medal.Gold: return "🥇";
                case Medal.Silver: return "🥈";
                case Medal.Bronze: return "🥉";
                default: return "・";
            }
        }

        /// <summary>Lv の上限。全部 🥇 のとき。</summary>
        public static int MaxLevel
        {
            get { return DrillSet.All().Count * Points(Medal.Gold); }
        }

        /// <summary>
        /// ID ごとの、人のプレイのベスト再現精度 %。記録として数えないもの（自動プレイ・速度を変えたもの）は入れない。
        /// </summary>
        public static Dictionary<string, double> BestById(IEnumerable<ReplayScore> scores)
        {
            var best = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var score in scores)
            {
                if (!score.Replay.Info.CountsAsRecord) continue;

                string id = DrillRecords.MapIdOf(score);
                if (id == null) continue;

                double current;
                if (!best.TryGetValue(id, out current) || score.ReproducibilityPercent > current)
                    best[id] = score.ReproducibilityPercent;
            }

            return best;
        }

        /// <summary>全方向の成績。並びは <see cref="DrillSet.Directions"/> のとおり。</summary>
        public static List<MedalRow> Evaluate(IDictionary<string, double> bestById)
        {
            if (bestById == null) throw new ArgumentNullException(nameof(bestById));

            var rows = new List<MedalRow>();
            foreach (var direction in DrillSet.Directions)
            {
                var cells = new List<MedalCell>();
                string name = null;

                foreach (var entry in DrillSet.Entries(direction))
                {
                    name = entry.Direction;
                    string id = DrillNaming.ExtractId(entry.Options.Name);

                    double percent;
                    bool played = bestById.TryGetValue(id, out percent);

                    cells.Add(new MedalCell
                    {
                        Entry = entry,
                        Id = id,
                        BestPercent = played ? percent : (double?)null,
                        Medal = played ? MedalOf(percent) : Medal.None,
                    });
                }

                rows.Add(new MedalRow { Direction = name, Cells = cells });
            }

            return rows;
        }
    }
}
