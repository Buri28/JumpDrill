using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JumpDrill.Parsing;

namespace JumpDrill.Model
{
    /// <summary>一括生成の1本。方向1つ × 速さ1 Stage。</summary>
    public sealed class DrillSetEntry
    {
        /// <summary>方向の名前。右手の遷移と左右反転した左手を詰めたもの（<c>R3bL2a</c>）。</summary>
        public string Direction { get; internal set; }

        /// <summary>何番目の Stage か。1 始まりで、30秒の Stage が最後。</summary>
        public int Stage { get; internal set; }

        /// <summary>片手の速さ（BPM 1/2）。倍率を掛けた後の実際の値。</summary>
        public double Bpm { get; internal set; }

        /// <summary>譜面名の頭に付ける印の中身（<c>R3bL2a 100</c>）。</summary>
        public string Tag { get; internal set; }

        /// <summary>生成に渡す指定。<see cref="DrillOptions.Name"/> まで埋めてある。</summary>
        public DrillOptions Options { get; internal set; }
    }

    /// <summary>
    /// 方向ごとの速さの Stage。どの Stage から挑んでもよく、
    /// 取れたメダル（再現精度 70 / 80 / 90%）の数で Lv が上がる。
    ///
    /// <b>Stage に順序の縛りを付けない。</b>ジャンプは遅いほど楽とは限らず
    /// （遅いと合わせるのが難しい）、1つ詰まると先が練習できない解放制は合わない。
    ///
    /// 各 Stage は右10秒 → 左10秒の片手ずつ。持久の Stage 8 として 350 BPM を右30秒 → 左30秒でも置く。
    /// 遠い配置と横の配置は同じ BPM では重すぎるので、Stage の BPM に倍率を掛ける。
    /// NJS は Stage で決め、倍率では変えない。
    ///
    /// 名前の頭の <c>{R3bL2a 100}</c> は、曲一覧が名前順なら方向ごと・速さ順にまとまるように
    /// ID より前に置く。BPM は3桁に揃える（50 が 100 より後ろに並ばないように）。
    /// </summary>
    public static class DrillSet
    {
        /// <summary>Stage の BPM（倍率を掛ける前）。</summary>
        public static readonly IReadOnlyList<double> StageBpm = new[] { 100.0, 150.0, 200.0, 250.0, 300.0, 350.0, 400.0 };

        /// <summary>Stage の NJS。<see cref="StageBpm"/> と同じ並び。</summary>
        public static readonly IReadOnlyList<double> StageNjs = new[] { 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0 };

        /// <summary>通常の Stage の尺（手あたり）。</summary>
        public const double ShortSeconds = 10.0;

        /// <summary>持久の Stage の尺（手あたり）。</summary>
        public const double LongSeconds = 30.0;

        /// <summary>持久の Stage に使う BPM の位置（0 始まり）。350 BPM。</summary>
        public const int LongStageIndex = 5;

        /// <summary>右手で書いた方向。左手は左右反転で作る。</summary>
        public static readonly IReadOnlyList<string> Directions = new[]
        {
            "3b", "4b", "4a", "49", "8b", "8a", "89", "86", "85", "ca", "c9", "38", "3c", "82",
        };

        /// <summary>遠い配置・横の配置の BPM 倍率。ここに無い方向は 1。</summary>
        private static readonly Dictionary<string, double> Factors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "49", 3.0 / 5.0 }, { "c9", 3.0 / 5.0 }, { "85", 3.0 / 5.0 }, { "82", 3.0 / 5.0 },
            { "89", 4.0 / 5.0 }, { "86", 4.0 / 5.0 }, { "ca", 4.0 / 5.0 },
        };

        /// <summary>方向の BPM 倍率。</summary>
        public static double Factor(string direction)
        {
            double factor;
            return Factors.TryGetValue(direction, out factor) ? factor : 1.0;
        }

        /// <summary>全方向・全 Stage。方向の並びは <see cref="Directions"/> のとおり。</summary>
        public static List<DrillSetEntry> All()
        {
            return Directions.SelectMany(Entries).ToList();
        }

        /// <summary>1方向ぶんの Stage。通常の Stage を速さ順に並べ、最後に持久の Stage。</summary>
        public static List<DrillSetEntry> Entries(string direction)
        {
            var right = SequenceParser.ParseOne("R" + direction);
            var left = SequenceParser.Mirror(right, Hand.Left);
            string name = right.ToString() + left.ToString();
            double factor = Factor(direction);

            var entries = new List<DrillSetEntry>();
            for (int i = 0; i < StageBpm.Count; i++)
                entries.Add(Entry(right, left, name, i + 1, StageBpm[i] * factor, StageNjs[i], ShortSeconds));

            entries.Add(Entry(right, left, name, StageBpm.Count + 1,
                StageBpm[LongStageIndex] * factor, StageNjs[LongStageIndex], LongSeconds));
            return entries;
        }

        private static DrillSetEntry Entry(HandSequence right, HandSequence left, string name,
            int stage, double bpm, double njs, double seconds)
        {
            var ci = CultureInfo.InvariantCulture;

            var options = new DrillOptions
            {
                Sequences = new[] { right, left },
                HandPattern = HandPattern.Split,
                // 片手ずつなら片手の間隔＝ノーツ間隔。BPM 1/2 で 30000/BPM。
                IntervalMs = Tempo.IntervalMsFromBpm(bpm, 2.0),
                NotesPerBeat = 2.0,
                DurationSeconds = seconds,
                Njs = njs,
            };

            string tag = string.Format(ci, "{0} {1:000}", name, Math.Round(bpm));
            if (seconds != ShortSeconds) tag += string.Format(ci, " {0:0.##}s", seconds);

            options.Name = "{" + tag + "} " + DrillNaming.Compose(options);
            options.Validate();

            return new DrillSetEntry
            {
                Direction = name,
                Stage = stage,
                Bpm = bpm,
                Tag = tag,
                Options = options,
            };
        }
    }
}
