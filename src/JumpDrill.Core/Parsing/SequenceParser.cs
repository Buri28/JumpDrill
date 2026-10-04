using System;
using System.Collections.Generic;
using System.Globalization;
using JumpDrill.Model;

namespace JumpDrill.Parsing
{
    /// <summary>
    /// 遷移記法のパース。
    /// <code>
    ///   R:8&gt;b, L:a&gt;1      右手 8↔b と左手 a↔1 を並行に
    ///   L:1&gt;9&gt;4&gt;c        左手の4点循環
    ///   R:8@dl&gt;b@ur        方向を明示（--dir explicit のとき）
    ///
    ///   R4b               短い形。: と &gt; は省ける
    ///   R4bs              b の矢印を上下左右に倒す（Straight / Square）
    ///   R4sbs             両端とも倒す
    /// </code>
    /// 手の接頭辞を省略すると右手。
    ///
    /// <b>短い形と <c>&gt;</c> の形は混ぜない。</b><c>&gt;</c> が1つでもあれば区切りの形として読む。
    /// 短い形で <c>@</c> は使えない。<c>4@ulb</c> の向きが "ul" なのか "ulb" なのか決められないため。
    /// </summary>
    public static class SequenceParser
    {
        /// <summary>カンマ区切りの複数手をまとめてパースする。</summary>
        public static List<HandSequence> ParseAll(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
                throw new FormatException(Lang.T("遷移の指定が空です。", "The sequence is empty."));

            var result = new List<HandSequence>();
            var seen = new HashSet<Hand>();

            foreach (var part in spec.Split(','))
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                var seq = ParseOne(part);
                if (!seen.Add(seq.Hand))
                    throw new FormatException(Lang.T("同じ手 (" + seq.Hand + ") の遷移が2回指定されています。", "The " + seq.Hand + " hand is specified twice."));
                result.Add(seq);
            }

            if (result.Count == 0)
                throw new FormatException(Lang.T("遷移の指定が空です。", "The sequence is empty."));
            return result;
        }

        public static HandSequence ParseOne(string spec)
        {
            var text = spec.Trim();
            var hand = Hand.Right;

            int colon = text.IndexOf(':');
            if (colon >= 0)
            {
                hand = ParseHand(text.Substring(0, colon).Trim());
                text = text.Substring(colon + 1).Trim();
            }
            else if (text.Length > 0 && "rRlL".IndexOf(text[0]) >= 0)
            {
                // 短い形の手の接頭辞。r / l はグリッド記号 (1-9 / a-c) に無いので取り違えない。
                hand = ParseHand(text.Substring(0, 1));
                text = text.Substring(1).Trim();
            }

            var steps = text.IndexOf('>') >= 0 ? ParseArrowForm(spec, text) : ParseCompactForm(spec, text);

            if (steps.Count < 2)
                throw new FormatException(Lang.T("'" + spec + "' は2点以上必要です（例: 8>b / 4b）。", "'" + spec + "' needs at least 2 points (e.g. 8>b / 4b)."));

            // 測るのは2点の間の往復（行きと帰りが同じ線を通るか）。3点以上を回ると往復が無く、
            // 再現性が出せないので記録に残らない。グリッド（GUI・MOD）と同じく片手2点までにする
            if (steps.Count > 2)
                throw new FormatException(Lang.T("'" + spec + "' は片手2点までです（例: 8b）。", "'" + spec + "' allows at most 2 points per hand (e.g. 8b)."));

            return new HandSequence(hand, steps);
        }

        /// <summary><c>8&gt;b&gt;c</c> の形。</summary>
        private static List<SequenceStep> ParseArrowForm(string spec, string text)
        {
            var steps = new List<SequenceStep>();

            foreach (var token in text.Split('>'))
            {
                var t = token.Trim();
                if (t.Length == 0)
                    throw new FormatException(Lang.T("'" + spec + "' に空の遷移点があります。", "'" + spec + "' has an empty point."));
                steps.Add(ParseStep(t));
            }

            return steps;
        }

        /// <summary>
        /// <c>4bs</c> の形。1文字が1点で、後ろに <c>s</c> が続けばその点を倒す。
        /// </summary>
        private static List<SequenceStep> ParseCompactForm(string spec, string text)
        {
            if (text.IndexOf('@') >= 0)
                throw new FormatException(Lang.T("'" + spec + "' は短い形なので @ は使えません。> の形で書いてください（例: R:8@dl>b@ur）。", "'" + spec + "' is in the short form, which does not allow @. Use the > form (e.g. R:8@dl>b@ur)."));

            var steps = new List<SequenceStep>();

            for (int i = 0; i < text.Length; )
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                GridPosition position;
                if (!GridPosition.TryParse(c, out position))
                    throw new FormatException(Lang.T("グリッド記号 '" + c + "' は 1-9 / a-c ではありません（'" + spec + "'）。", "Grid symbol '" + c + "' must be 1-9 / a-c ('" + spec + "')."));
                i++;

                bool straighten = false;
                while (i < text.Length && (text[i] == 's' || text[i] == 'S')) { straighten = true; i++; }

                steps.Add(new SequenceStep(position, null, straighten));
            }

            return steps;
        }

        private static SequenceStep ParseStep(string token)
        {
            string posPart = token;
            CutDirection? dir = null;
            bool straighten = false;

            int at = token.IndexOf('@');
            if (at >= 0)
            {
                posPart = token.Substring(0, at).Trim();
                dir = ParseDirection(token.Substring(at + 1).Trim());
            }
            else
            {
                // 末尾の s。@ と併記されたら、どちらを採るか決められないので弾く。
                while (posPart.Length > 1 && (posPart[posPart.Length - 1] == 's' || posPart[posPart.Length - 1] == 'S'))
                {
                    straighten = true;
                    posPart = posPart.Substring(0, posPart.Length - 1);
                }
            }

            if (posPart.Length != 1)
                throw new FormatException(Lang.T("グリッド記号 '" + posPart + "' は1文字 (1-9 / a-c) で書きます。", "Grid symbol '" + posPart + "' must be a single character (1-9 / a-c)."));

            return new SequenceStep(GridPosition.Parse(posPart[0]), dir, straighten);
        }

        public static Hand ParseHand(string text)
        {
            switch (text.ToLowerInvariant())
            {
                case "r":
                case "right":
                case "blue":
                    return Hand.Right;
                case "l":
                case "left":
                case "red":
                    return Hand.Left;
                default:
                    throw new FormatException(Lang.T("手の指定 '" + text + "' は R / L です。", "Hand '" + text + "' must be R or L."));
            }
        }

        /// <summary>方向名 (u/d/l/r/ul/ur/dl/dr/dot) または 0-8 の数値。</summary>
        public static CutDirection ParseDirection(string text)
        {
            int numeric;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric))
            {
                if (numeric < 0 || numeric > 8)
                    throw new FormatException(Lang.T("切る方向の数値は 0-8 です。", "Cut direction number must be 0-8."));
                return (CutDirection)numeric;
            }

            switch (text.ToLowerInvariant())
            {
                case "u": case "up": return CutDirection.Up;
                case "d": case "down": return CutDirection.Down;
                case "l": case "left": return CutDirection.Left;
                case "r": case "right": return CutDirection.Right;
                case "ul": case "upleft": return CutDirection.UpLeft;
                case "ur": case "upright": return CutDirection.UpRight;
                case "dl": case "downleft": return CutDirection.DownLeft;
                case "dr": case "downright": return CutDirection.DownRight;
                case "dot": case "any": case "-": case "x": return CutDirection.Any;
                default:
                    throw new FormatException(Lang.T("切る方向 '" + text + "' が解釈できません。", "Cannot read cut direction '" + text + "'."));
            }
        }

        /// <summary>左右反転した遷移を作る（反対の手の練習を対称に組むため）。</summary>
        public static HandSequence Mirror(HandSequence source, Hand newHand)
        {
            var steps = new List<SequenceStep>(source.Length);
            foreach (var step in source.Steps)
            {
                var p = new GridPosition(GridPosition.Columns - 1 - step.Position.LineIndex, step.Position.LineLayer);
                CutDirection? dir = null;
                if (step.ExplicitDirection.HasValue)
                {
                    int dx, dy;
                    step.ExplicitDirection.Value.ToVector(out dx, out dy);
                    dir = step.ExplicitDirection.Value == CutDirection.Any
                        ? CutDirection.Any
                        : Geometry.FromVector(-dx, dy);
                }
                steps.Add(new SequenceStep(p, dir, step.Straighten));
            }
            return new HandSequence(newHand, steps);
        }
    }
}
