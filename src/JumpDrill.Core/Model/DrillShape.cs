using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using JumpDrill.Parsing;

namespace JumpDrill.Model
{
    /// <summary>
    /// 行の方向（<see cref="DrillNaming.DirectionOf"/> の形、<c>R8bL5a tower alt flick25</c>）から読み戻した生成の指定。
    /// 速さ・尺・NJS は方向に入っていないので持たない。
    /// </summary>
    public sealed class DrillShape
    {
        public List<HandSequence> Sequences { get; private set; }
        public DirectionMode Direction { get; private set; }
        public bool Tower { get; private set; }
        public HandPattern HandPattern { get; private set; }
        public double FlickPercent { get; private set; }

        /// <summary>読み戻す。遷移が読めなければ null。</summary>
        public static DrillShape Parse(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction)) return null;

            var words = direction.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // R8bL5a → "R8b, L5a"
            var hands = Regex.Matches(words[0], "[RL][^RL]+");
            if (hands.Count == 0 || string.Concat(MatchValues(hands)) != words[0]) return null;

            List<HandSequence> sequences;
            try { sequences = SequenceParser.ParseAll(string.Join(", ", MatchValues(hands))); }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }

            var shape = new DrillShape
            {
                Sequences = sequences,
                Direction = DirectionMode.Axis,
                HandPattern = HandPattern.Split,
            };

            for (int i = 1; i < words.Length; i++)
            {
                string w = words[i];
                switch (w)
                {
                    case "perp": shape.Direction = DirectionMode.Perpendicular; break;
                    case "perp-outer": shape.Direction = DirectionMode.PerpendicularOuter; break;
                    case "dot": shape.Direction = DirectionMode.Dot; break;
                    case "explicit": shape.Direction = DirectionMode.Explicit; break;
                    case "tower": shape.Tower = true; break;
                    case "sync": shape.HandPattern = HandPattern.Sync; break;
                    case "alt": shape.HandPattern = HandPattern.Alternate; break;
                    case "alt-trip": shape.HandPattern = HandPattern.AlternateRoundTrip; break;
                    case "split": shape.HandPattern = HandPattern.Split; break;
                    default:
                        double flick;
                        if (w.StartsWith("flick", StringComparison.Ordinal) &&
                            double.TryParse(w.Substring(5), NumberStyles.Float, CultureInfo.InvariantCulture, out flick))
                            shape.FlickPercent = flick;
                        else
                            return null;   // 知らない語（ユーザーが付けた行名など）は読み戻さない
                        break;
                }
            }

            return shape;
        }

        /// <summary>
        /// 2本目の手が1本目の左右反転か。グリッドは片手ぶん組んで反転する作りなので、
        /// 反転でない両手の形はグリッドに戻せない。
        /// </summary>
        public bool IsMirrored
        {
            get
            {
                if (Sequences.Count != 2 || Sequences[0].Hand == Sequences[1].Hand) return false;
                var mirrored = SequenceParser.Mirror(Sequences[0], Sequences[1].Hand);
                return mirrored.ToString() == Sequences[1].ToString();
            }
        }

        private static IEnumerable<string> MatchValues(MatchCollection matches)
        {
            foreach (Match m in matches) yield return m.Value;
        }
    }
}
