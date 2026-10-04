using System;
using System.Globalization;
using System.Text;

namespace JumpDrill.Model
{
    /// <summary>
    /// ノーツ間隔と BPM の換算。
    ///
    /// 設計メモ §2 のとおり、間隔の指定そのものは ms で持つ。BPM だけでは
    /// 細分化（1拍あたり何ノーツか）が決まらず、同じ「300 BPM」が
    /// 1/1 なら 200ms、1/2 なら 100ms と倍違ってしまうため。
    /// ここでは常に notesPerBeat を明示させることでその曖昧さを潰している。
    /// </summary>
    public static class Tempo
    {
        /// <summary>BPM と細分化からノーツ間隔 (ms) を出す。</summary>
        public static double IntervalMsFromBpm(double bpm, double notesPerBeat = 1.0)
        {
            if (bpm <= 0) throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "BPM は正の値。");
            if (notesPerBeat <= 0) throw new ArgumentOutOfRangeException(nameof(notesPerBeat), notesPerBeat, "1拍あたりのノーツ数は正の値。");

            return 60000.0 / (bpm * notesPerBeat);
        }

        /// <summary>ノーツ間隔 (ms) と細分化から BPM を出す。</summary>
        public static double BpmFromIntervalMs(double intervalMs, double notesPerBeat = 1.0)
        {
            if (intervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMs), intervalMs, "ノーツ間隔は正の値。");
            if (notesPerBeat <= 0) throw new ArgumentOutOfRangeException(nameof(notesPerBeat), notesPerBeat, "1拍あたりのノーツ数は正の値。");

            return 60000.0 / (intervalMs * notesPerBeat);
        }

        /// <summary>1分あたりのノーツ数。1/1 で数えたときの BPM と同じ値。</summary>
        public static double NotesPerMinute(double intervalMs)
        {
            if (intervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMs), intervalMs, "ノーツ間隔は正の値。");
            return 60000.0 / intervalMs;
        }

        /// <summary>
        /// EBPM（Effective BPM）。ScoreSaber のランク基準はこの単位で書かれている。
        ///
        /// 定義は「<b>片手が、その BPM に対して 1/2 の精度で動く速さ</b>」。
        /// つまり 1拍 = 60000/EBPM ms、その半分ごとに片手が1回振る。
        /// <code>
        ///   EBPM = 30000 / 片手の間隔(ms)
        /// </code>
        /// 例: 100 BPM の曲の 1/4 精度・片手モーション（片手 150ms）は 200 EBPM。
        ///
        /// <b>両手を合わせた流れの速さではない。</b>片手が何 ms ごとに振るかで決まる。
        /// </summary>
        public static double EbpmFromHandIntervalMs(double handIntervalMs)
        {
            if (handIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(handIntervalMs), handIntervalMs, "間隔は正の値。");
            return 30000.0 / handIntervalMs;
        }

        /// <summary>EBPM から片手の間隔 (ms) を出す。</summary>
        public static double HandIntervalMsFromEbpm(double ebpm)
        {
            if (ebpm <= 0) throw new ArgumentOutOfRangeException(nameof(ebpm), ebpm, "EBPM は正の値。");
            return 30000.0 / ebpm;
        }

        /// <summary>よく使う細分化での BPM を並べた1行。実曲と突き合わせるため。</summary>
        public static string Describe(double intervalMs)
        {
            var sb = new StringBuilder();
            foreach (int div in new[] { 1, 2, 4 })
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.#} BPM 1/{1}",
                    BpmFromIntervalMs(intervalMs, div), div);
            }
            return sb.ToString();
        }
    }
}
