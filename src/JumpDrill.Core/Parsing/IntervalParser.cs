using System;
using System.Globalization;
using JumpDrill.Model;

namespace JumpDrill.Parsing
{
    /// <summary>
    /// ノーツ間隔の指定をパースする。単位を値に付けられるようにして、
    /// ms と BPM のどちらで考えていてもそのまま書けるようにする。
    ///
    /// <code>
    ///   110       110 ms
    ///   110ms     110 ms
    ///   300ebpm   片手が 300 EBPM       → 100 ms
    ///   300bpm    1拍1ノーツで 300 BPM  → 200 ms
    ///   300bpm    notesPerBeat=2 なら    → 100 ms
    /// </code>
    ///
    /// ScoreSaber のランク基準は EBPM で書かれている。定義は
    /// 「片手が、その BPM に対して 1/2 の精度で動く速さ」＝ <c>30000 / 片手の間隔(ms)</c>。
    /// 細分化を別に言う必要がない。
    ///
    /// ここで換算するのは<b>片手の間隔</b>。片手ずつ振るドリル（既定）ではそのまま
    /// ノーツ間隔になるが、両手交互（<c>--hands alt</c>）では片手の間隔が倍になる。
    /// </summary>
    public static class IntervalParser
    {
        /// <param name="notesPerBeat">
        /// bpm 指定のときの細分化（1拍あたりのノーツ数）。ms 指定では使わない。
        /// </param>
        public static double ParseIntervalMs(string text, double notesPerBeat = 1.0)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException(Lang.T("ノーツ間隔の指定が空です。", "The note interval is empty."));

            string trimmed = text.Trim();
            string lower = trimmed.ToLowerInvariant();

            bool isBpm = false;
            bool isEbpm = false;
            string number = lower;

            // ebpm を先に見る。bpm で切ると "350e" が残って読めなくなる。
            if (lower.EndsWith("ebpm", StringComparison.Ordinal))
            {
                isEbpm = true;
                number = lower.Substring(0, lower.Length - 4);
            }
            else if (lower.EndsWith("bpm", StringComparison.Ordinal))
            {
                isBpm = true;
                number = lower.Substring(0, lower.Length - 3);
            }
            else if (lower.EndsWith("ms", StringComparison.Ordinal))
            {
                number = lower.Substring(0, lower.Length - 2);
            }

            number = number.Trim();

            double value;
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException(Lang.T("ノーツ間隔 '" + trimmed + "' が数値として読めません。（例: 110 / 110ms / 300ebpm / 300bpm）", "Cannot read note interval '" + trimmed + "' as a number (e.g. 110 / 110ms / 300ebpm / 300bpm)."));

            if (value <= 0)
                throw new FormatException(Lang.T("ノーツ間隔は正の値で指定してください: '" + trimmed + "'", "Note interval must be positive: '" + trimmed + "'"));

            // EBPM は片手の速さ。細分化は畳み込まれている。
            if (isEbpm) return Tempo.HandIntervalMsFromEbpm(value);

            return isBpm ? Tempo.IntervalMsFromBpm(value, notesPerBeat) : value;
        }

        /// <summary>
        /// 細分化の指定。「1拍あたりのノーツ数」でも「音符」でも書けるようにする。
        ///
        /// <code>
        ///   2      1拍に2ノーツ
        ///   1/2    8分。1拍に2ノーツ（上と同じ）
        ///   1/4    16分。1拍に4ノーツ
        /// </code>
        ///
        /// 頭の中では「1/2 拍」と数えているのに欄が「ノーツ/拍」だと
        /// 2 と 1/2 のどちらを入れるのか毎回迷うので、両方通す。
        /// </summary>
        public static double ParseNotesPerBeat(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException(Lang.T("細分化の指定が空です。", "The subdivision is empty."));

            string trimmed = text.Trim();
            int slash = trimmed.IndexOf('/');

            double value;
            if (slash >= 0)
            {
                double numerator, denominator;
                if (!double.TryParse(trimmed.Substring(0, slash).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numerator) ||
                    !double.TryParse(trimmed.Substring(slash + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out denominator) ||
                    numerator == 0)
                    throw new FormatException(Lang.T("細分化 '" + trimmed + "' が読めません。（例: 2 / 1/2 / 1/4）", "Cannot read subdivision '" + trimmed + "' (e.g. 2 / 1/2 / 1/4)."));

                // 1/2 は「1拍を2つに割る」＝ 1拍に2ノーツ。
                value = denominator / numerator;
            }
            else if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new FormatException(Lang.T("細分化 '" + trimmed + "' が読めません。（例: 2 / 1/2 / 1/4）", "Cannot read subdivision '" + trimmed + "' (e.g. 2 / 1/2 / 1/4)."));
            }

            if (value <= 0)
                throw new FormatException(Lang.T("細分化は正の値で指定してください: '" + trimmed + "'", "Subdivision must be positive: '" + trimmed + "'"));

            return value;
        }

        /// <summary>bpm 単位で書かれているか。表示の単位を合わせるために使う。</summary>
        public static bool IsBpm(string text)
        {
            return text != null &&
                   text.Trim().EndsWith("bpm", StringComparison.OrdinalIgnoreCase);
        }
    }
}
