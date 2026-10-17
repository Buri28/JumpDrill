using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace JumpDrill.Model
{
    /// <summary>
    /// 譜面名の組み立てと読み戻し。CLI と GUI で同じ名前になるよう、ここ1か所で作る。
    ///
    /// <code>
    ///   [Q7K3M] Drill R8b L5a axis 300 BPM 30s
    ///   [Q7K3M] Drill R8b L5a axis 300 BPM 30s x3      セットを繰り返すとき
    ///   [Q7K3M] Drill R8b L5a axis sync 300 BPM 30s    両手同時（片手ずつ以外の配置は印が付く）
    ///   [Q7K3M] Drill R3b L2a axis tower 300 BPM 30s    タワー
    ///   [Q7K3M] Drill R3b L2a axis alt-trip 300 BPM 30s 往復ごとに交互
    ///   [Q7K3M] Drill R3b L2a axis tight25 300 BPM 30s  間隔を詰める 25%
    ///   {R8bL5a 300} [Q7K3M] Drill R8b L5a axis 300 BPM 10s   一括生成の1本
    /// </code>
    ///
    /// 速さは入れた BPM（ノーツ間隔を 1/2 で数えた値）を書く。<c>30000 / ノーツ間隔(ms)</c>。
    /// ジャンプ（同時・片手ずつ）や往復ごとに交互では片手の速さと同じで、ScoreSaber の「350 BPM」と同じ数え方になる。
    /// 両手交互（alt）だけは片手の速さがその半分（ID は片手の間隔から作る）。
    /// 2026-10-17 までの alt は片手の速さを書いていた（<see cref="ParseBpm"/> はどちらも読める）。
    /// 以前は同じ値を「EBPM」と書いていた。
    ///
    /// 頭の <c>{…}</c> は並び順の印（<see cref="JumpDrill.Model.DrillSet"/>）。
    /// 曲一覧が名前順なら方向ごと・速さ順にまとまるよう、ID より前に置く。
    ///
    /// 尺（<c>30s</c>）とセット数（<c>x3</c>）も名前に入れる。同じ配置・同じ速さでも
    /// 30 秒と 60 秒は別のドリルで、曲一覧で見分けが付かないと選べない。
    /// セット数は既定の 1 のときは書かない。
    ///
    /// 頭の <b>ID</b> は「遷移・向き・片手の間隔」から計算する決定的な符号。
    /// リプレイから譜面へ辿れるのは曲名だけなので、記録をまとめる鍵をそこに埋める。
    /// <b>連番ではなく設定から決まる</b>ので、同じドリルを作り直しても同じ ID になり、
    /// 履歴が切れない。表示の書式を将来変えても ID は変わらない。
    /// </summary>
    public static class DrillNaming
    {
        /// <summary>譜面名の頭に付ける語。リプレイをドリルだけに絞るのに使う。</summary>
        public const string Prefix = "Drill ";

        /// <summary>ID の文字数。32^5 ≒ 3300 万通りで、譜面が数百あってもぶつからない。</summary>
        public const int IdLength = 5;

        /// <summary>
        /// ID に使う文字。Crockford の base32 で、読み違えやすい I L O U を外してある。
        /// </summary>
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /// <summary>
        /// 片手の間隔 (ms)。<b>ノーツ間隔とは限らない。</b> ID はこの値から作る。
        ///
        /// 両手交互（<c>alt</c>）では自分の番が手の数ぶんおきにしか回ってこないので、
        /// 片手の間隔はノーツ間隔の手数倍になる。同時・片手ずつはノーツ間隔と同じ。
        /// 名前に書く BPM（<see cref="NameBpm"/>）はノーツ間隔から出すので、alt では食い違う。
        /// </summary>
        public static double HandIntervalMs(DrillOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            return options.HandPattern == HandPattern.Alternate
                ? options.IntervalMs * options.Sequences.Count
                : options.IntervalMs;
        }

        /// <summary>
        /// 譜面名に書く BPM。ノーツ間隔を 1/2 で数えた値（入れた BPM）。
        /// split・sync・alt-trip では片手の速さ（EBPM）と同じ。alt だけは片手の速さの手数倍になる
        /// （2026-10-17 まで alt は片手の速さを書いていた。<see cref="ParseBpm"/> はどちらも読める）。
        /// </summary>
        public static double NameBpm(DrillOptions options)
        {
            return Tempo.EbpmFromHandIntervalMs(options.IntervalMs);
        }

        /// <summary>
        /// 譜面名を組み立てる。
        /// </summary>
        /// <param name="explicitBody">遷移と向きの代わりに使う文字列。<c>--name</c> 指定。</param>
        public static string Compose(DrillOptions options, string explicitBody = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var ci = CultureInfo.InvariantCulture;
            // タワーは別のドリルなので名前（と ID）を分ける。
            // 両手の配置（同時・交互・往復ごとに交互）は叩き方が別物なので名前を分ける。
            // 片手ずつは名前に入れない（標準のステージの名前と ID、今までの記録をそのまま保つ）。
            // 間隔を詰めるのも叩き方が違うので名前を分ける（tight25 のように割合を添える）。
            string body = explicitBody ?? string.Format(ci, "{0} {1}{2}{3}{4}",
                string.Join(" ", options.Sequences.Select(s => s.ToString()).ToArray()),
                DirectionWord(options.Direction),
                options.Tower ? TowerWord : "",
                PatternWord(options),
                options.FlickPercent > 0 ? string.Format(ci, " {0}{1:0.#}", TightPrefix, options.FlickPercent) : "");

            // 速さは 1 桁だけ小数を残す。整数に丸めると 171ms が 175 BPM = 171.4ms に戻り、
            // 「片手 171 ms」と出したいところが 171.4 ms になる。
            string speed = string.Format(ci, "{0:0.#} BPM", NameBpm(options));
            string length = Length(options.DurationSeconds, options.Sets);

            return string.Format(ci, "[{0}] {1}{2} {3} {4}",
                MakeId(body, HandIntervalMs(options), options.DurationSeconds, options.Sets),
                Prefix, body, speed, length);
        }

        /// <summary>
        /// 名前に書く切る方向。既存のものは enum の名前の小文字のまま（変えると ID が変わる）。
        /// </summary>
        private static string DirectionWord(DirectionMode mode)
        {
            return mode == DirectionMode.PerpendicularOuter
                ? "perpendicular-outer"
                : mode.ToString().ToLowerInvariant();
        }

        /// <summary>尺の表記。セットが1組だけなら回数は書かない。</summary>
        private static string Length(double seconds, int sets)
        {
            var ci = CultureInfo.InvariantCulture;

            string text = string.Format(ci, "{0:0.##}s", seconds);
            return sets > 1 ? text + string.Format(ci, " x{0}", sets) : text;
        }

        /// <summary>
        /// 記録をまとめる鍵。<b>名前に出ているものだけ</b>から作る。
        ///
        /// 同じ名前で出るものは同じ ID になり、名前が違えば ID も違う。
        /// 入れるものを増やすと過去の記録と一致しなくなるので、名前と一緒にしか動かさない。
        /// </summary>
        /// <param name="body">遷移と向き。例 <c>R8b L5a axis</c>。</param>
        /// <param name="handIntervalMs">片手の間隔 (ms)。整数に丸めて使う。</param>
        public static string MakeId(string body, double handIntervalMs, double seconds, int sets)
        {
            var ci = CultureInfo.InvariantCulture;

            string canonical = Normalize(body) + "|" +
                Math.Round(handIntervalMs).ToString("0", ci) + "|" +
                seconds.ToString("0.##", ci) + "|" +
                sets.ToString(ci);

            return Encode(Fnv1a(canonical));
        }

        /// <summary>
        /// ID を入れる前の名前（<c>Drill R:8&gt;b L:5&gt;a axis 100ms</c>）から作る鍵。
        ///
        /// 当時の名前は<b>尺もセット数も持っていない</b>ので、いまの <see cref="MakeId"/> とは
        /// 別の式になる。つまり<b>昔の記録は、いま作る譜面とは別の行に積み上がる</b>。
        /// 尺が違えば別のドリルと決めた以上、分からないものを同じ物として混ぜられない。
        /// 昔の記録どうしは、これまでどおり1つにまとまる。
        /// </summary>
        public static string LegacyId(string body, double handIntervalMs)
        {
            string canonical = Normalize(body) + "|" +
                Math.Round(handIntervalMs).ToString("0", CultureInfo.InvariantCulture);

            return Encode(Fnv1a(canonical));
        }

        /// <summary>
        /// 遷移の字面を<b>いまの記法</b>に揃える。
        ///
        /// 昔の名前は <c>R:8&gt;b</c> と書いてあった。いまは <c>R8b</c> なので、
        /// そのまま鍵にすると同じドリルが別の ID になって履歴が割れる。
        /// <c>:</c> か <c>&gt;</c> を含む語だけ読み直して書き戻す。
        /// （<c>--name</c> で入れた文字にはこの2文字が出てこないので、巻き込まない）
        /// </summary>
        private static string Normalize(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";

            string trimmed = body.Trim();
            if (trimmed.IndexOf(':') < 0 && trimmed.IndexOf('>') < 0) return trimmed;

            var parts = trimmed.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].IndexOf(':') < 0 && parts[i].IndexOf('>') < 0) continue;

                try { parts[i] = JumpDrill.Parsing.SequenceParser.ParseOne(parts[i]).ToString(); }
                catch (FormatException) { }
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// 並び順の印（<c>{R8bL5a 300}</c> の中身）。付いていなければ null。
        /// </summary>
        public static string ExtractSetTag(string songName)
        {
            if (string.IsNullOrEmpty(songName) || songName[0] != '{') return null;
            int close = songName.IndexOf('}');
            return close > 0 ? songName.Substring(1, close - 1) : null;
        }

        /// <summary>並び順の印を読み飛ばした先の位置。印が無ければ 0。</summary>
        private static int AfterSetTag(string songName)
        {
            if (ExtractSetTag(songName) == null) return 0;

            int i = songName.IndexOf('}') + 1;
            while (i < songName.Length && songName[i] == ' ') i++;
            return i;
        }

        /// <summary>
        /// 譜面名に埋めた ID。付いていなければ null。
        /// 頭に並び順の印があれば、その後ろを見る。
        /// </summary>
        public static string ExtractId(string songName)
        {
            if (string.IsNullOrEmpty(songName)) return null;

            int s = AfterSetTag(songName);
            if (songName.Length < s + IdLength + 2 || songName[s] != '[') return null;
            if (songName[s + IdLength + 1] != ']') return null;

            for (int i = 1; i <= IdLength; i++)
                if (Alphabet.IndexOf(songName[s + i]) < 0) return null;

            return songName.Substring(s + 1, IdLength);
        }

        /// <summary>
        /// 画面に出す名前。ID を落とし、BPM を今の書き方（ノーツ間隔を 1/2 で数えた値）にそろえる。
        /// 以前の alt の名前は片手の速さ（半分）で書いてあるので、そこだけ直して出す。譜面名と ID は変えない。
        /// </summary>
        public static string DisplayName(string songName)
        {
            return StripId(CurrentName(songName));
        }

        /// <summary>
        /// 今の書き方の曲名。以前の書き方の alt（BPM が片手の速さ）なら BPM をノーツの流れの値に直す。
        /// ID は片手の間隔から作るので変わらない。それ以外はそのまま返す。
        /// </summary>
        public static string CurrentName(string songName)
        {
            if (string.IsNullOrEmpty(songName) || !IsPerHandAlternateName(songName)) return songName;

            var words = songName.Split(' ');
            int bpm = Array.IndexOf(words, "BPM");
            if (bpm < 1) return songName;
            words[bpm - 1] = ParseBpm(songName).Value.ToString("0.#", CultureInfo.InvariantCulture);
            return string.Join(" ", words);
        }

        /// <summary>
        /// 名前の BPM から片手の間隔を出すときに掛ける数。今の書き方の alt だけ手の数、ほかは 1。
        /// </summary>
        public static int NameHandMultiplier(string songName)
        {
            int hands = AlternateHands(songName);
            return hands >= 2 && !IsPerHandAlternateName(songName) ? hands : 1;
        }

        /// <summary>両手交互（alt）の手の数。alt でなければ 1。</summary>
        private static int AlternateHands(string songName)
        {
            if (string.IsNullOrEmpty(songName) || !HasWord(songName, AltWord.Trim())) return 1;
            return Math.Max(1, ParseSequences(songName).Count);
        }

        /// <summary>
        /// 以前の書き方の alt の名前（BPM が片手の速さ）か。
        /// ID は片手の間隔から作っているので、名前の BPM を片手の速さとして読んで ID が合えば以前の書き方。
        /// </summary>
        private static bool IsPerHandAlternateName(string songName)
        {
            if (AlternateHands(songName) < 2) return false;

            string id = ExtractId(songName);
            double? raw = RawBpm(songName);
            string body = NameBody(songName);
            double seconds;
            int sets;
            if (id == null || !raw.HasValue || raw.Value <= 0 || body == null || !ParseLength(songName, out seconds, out sets)) return false;

            return MakeId(body, Tempo.HandIntervalMsFromEbpm(raw.Value), seconds, sets) == id;
        }

        /// <summary>名前の遷移と向き（<c>R8b L5a axis alt</c>）。ID を作るときの body と同じ。読めなければ null。</summary>
        private static string NameBody(string songName)
        {
            string name = StripId(songName).Trim();
            if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(Prefix.Length);

            var words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int bpm = Array.IndexOf(words, "BPM");
            if (bpm < 2) return null;
            return string.Join(" ", words, 0, bpm - 1);
        }

        /// <summary>曲名の <c>BPM</c> の後ろの尺（<c>30s</c>）とセット数（<c>x3</c>、無ければ 1）。</summary>
        public static bool ParseLength(string songName, out double seconds, out int sets)
        {
            seconds = 0;
            sets = 1;
            if (string.IsNullOrEmpty(songName)) return false;

            var words = StripId(songName).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int bpm = Array.IndexOf(words, "BPM");
            if (bpm < 0 || bpm + 1 >= words.Length) return false;

            string length = words[bpm + 1];
            if (!length.EndsWith("s", StringComparison.Ordinal) ||
                !double.TryParse(length.Substring(0, length.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) ||
                seconds <= 0)
                return false;

            if (bpm + 2 < words.Length && words[bpm + 2].StartsWith("x", StringComparison.Ordinal))
            {
                int parsed;
                if (int.TryParse(words[bpm + 2].Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                    sets = parsed;
            }
            return true;
        }

        /// <summary>
        /// ID（と並び順の印）を落とした表示用の名前。ID が無ければそのまま返す。
        /// </summary>
        public static string StripId(string songName)
        {
            if (ExtractId(songName) == null) return songName;
            return songName.Substring(AfterSetTag(songName) + IdLength + 2).TrimStart();
        }

        /// <summary>
        /// 譜面名の BPM（<c>300 BPM</c> の 300）。ノーツ間隔を 1/2 で数えた値で、書いていなければ null。
        /// 以前の書き方の alt（片手の速さ）は手の数を掛けて今の書き方にそろえる。
        /// カスタムステージのように、生成の指定を持たずに名前だけで並べるときに使う。
        /// </summary>
        public static double? ParseBpm(string songName)
        {
            double? raw = RawBpm(songName);
            if (!raw.HasValue) return null;
            return IsPerHandAlternateName(songName) ? raw.Value * AlternateHands(songName) : raw;
        }

        /// <summary>譜面名に書いてある BPM の数字そのもの。</summary>
        private static double? RawBpm(string songName)
        {
            if (string.IsNullOrEmpty(songName)) return null;

            var words = StripId(songName).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < words.Length; i++)
            {
                double bpm;
                if (words[i] == "BPM" &&
                    double.TryParse(words[i - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out bpm))
                    return bpm;
            }
            return null;
        }

        /// <summary>
        /// 譜面名に書いた遷移（<c>Drill R8b L5a axis …</c> の R8b と L5a）。
        /// 読めなければ空。<c>--name</c> で名前を変えた譜面は読めない。
        /// </summary>
        public static List<HandSequence> ParseSequences(string songName)
        {
            var result = new List<HandSequence>();
            if (string.IsNullOrEmpty(songName)) return result;

            string body = StripId(songName).Trim();
            if (body.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                body = body.Substring(Prefix.Length);

            foreach (var word in body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try { result.Add(JumpDrill.Parsing.SequenceParser.ParseOne(word)); }
                catch (FormatException) { break; }
                catch (ArgumentException) { break; }
            }
            return result;
        }

        /// <summary>
        /// 譜面名から読んだ方向（遷移を詰めたもの、<c>R8bL5a</c>）。読めなければ空文字。
        /// カスタムステージの1行に同じ方向のドリルだけを並べるのに使う。
        /// 切る方向（axis 以外）・タワー・配置・間隔を詰めるのも振り方が違うので別の方向として扱う
        /// （<c>R3b dot tower</c>）。axis は印なし（今までの行をそのまま保つ）。
        /// 切る方向は短く書く（perp / perp-outer / dot / explicit）。行名に出るので長いと切れる。
        /// </summary>
        public static string DirectionOf(string songName)
        {
            string direction = string.Concat(ParseSequences(songName).Select(s => s.ToString()));
            if (direction.Length == 0) return direction;
            foreach (var pair in CutWords)
                if (HasWord(songName, pair[0])) { direction += " " + pair[1]; break; }
            if (IsTower(songName)) direction += TowerWord;
            foreach (var word in PatternWords)
                if (HasWord(songName, word.Trim())) direction += word;
            string tight = TightWord(songName);
            if (tight != null) direction += " " + tight;
            return direction;
        }

        /// <summary>
        /// 行名（<see cref="DirectionOf"/> の形、<c>R3bL2a tower tight25</c>）を、方向と後ろの印に分ける。
        /// 印は切る方向（perp・perp-outer・dot・explicit）・tower・sync・alt・alt-trip・tight〈数字〉だけ。ほかの語（ユーザーが付けた「My jumps」など）は分けずに名前のまま返す。
        /// 両手の方向（<c>R8bL5a</c>）で配置の印が無ければ片手ずつなので、表示用に <c>split</c> を足す
        /// （譜面名には書かない。標準のステージの名前と ID を保つため）。
        /// </summary>
        public static void SplitTags(string rowName, out string name, out string tags)
        {
            name = rowName ?? "";
            tags = "";

            var words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int first = words.Length;
            while (first > 1 && IsTagWord(words[first - 1])) first--;

            var tagWords = words.Skip(first).ToList();
            if (first == 1 && IsBothHands(words[0]) && !tagWords.Any(w => PatternWords.Any(p => p.Trim() == w)))
            {
                // 配置の印と同じ位置（間隔を詰める印の前）に入れる
                int at = tagWords.FindIndex(w => w.StartsWith(TightPrefix, StringComparison.Ordinal));
                tagWords.Insert(at < 0 ? tagWords.Count : at, SplitWord);
            }
            if (tagWords.Count == 0) return;

            name = string.Join(" ", words, 0, first);
            tags = string.Join(" ", tagWords);
        }

        /// <summary>片手ずつの印。表示だけに使う。</summary>
        private const string SplitWord = "split";

        /// <summary>両手の方向（<c>R8bL5a</c>）か。</summary>
        private static bool IsBothHands(string direction)
        {
            return Regex.IsMatch(direction, "^(?:[RL](?:[1-9a-c]s?){1,2})+$")
                && direction.IndexOf('R') >= 0 && direction.IndexOf('L') >= 0;
        }

        private static bool IsTagWord(string word)
        {
            if (word == TowerWord.Trim()) return true;
            foreach (var pair in CutWords)
                if (word == pair[1]) return true;
            foreach (var pattern in PatternWords)
                if (word == pattern.Trim()) return true;
            if (!word.StartsWith(TightPrefix, StringComparison.Ordinal) || word.Length == TightPrefix.Length) return false;

            double percent;
            return double.TryParse(word.Substring(TightPrefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
        }

        /// <summary>間隔を詰める印の頭。後ろに割合が付く（<c>tight25</c>）。</summary>
        private const string TightPrefix = "tight";

        /// <summary>名前の <c>BPM</c> より前にある間隔を詰める印（<c>tight25</c>）。無ければ null。</summary>
        private static string TightWord(string songName)
        {
            if (string.IsNullOrEmpty(songName)) return null;

            foreach (var word in StripId(songName).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word == "BPM") return null;
                if (word.StartsWith(TightPrefix, StringComparison.Ordinal) && word.Length > TightPrefix.Length) return word;
            }
            return null;
        }

        /// <summary>譜面名の切る方向の語と、行名に出す短い印。axis は印なし。</summary>
        private static readonly string[][] CutWords =
        {
            new[] { "perpendicular", "perp" },
            new[] { "perpendicular-outer", "perp-outer" },
            new[] { "dot", "dot" },
            new[] { "explicit", "explicit" },
        };

        /// <summary>名前の向きの後ろに入るタワーの印。</summary>
        private const string TowerWord = " tower";

        /// <summary>往復ごとに交互（<see cref="HandPattern.AlternateRoundTrip"/>）の印。タワーの印の後ろに入る。</summary>
        private const string AltTripWord = " alt-trip";

        /// <summary>同時（<see cref="HandPattern.Sync"/>）の印。</summary>
        private const string SyncWord = " sync";

        /// <summary>1ステップごとの交互（<see cref="HandPattern.Alternate"/>）の印。</summary>
        private const string AltWord = " alt";

        private static readonly string[] PatternWords = { SyncWord, AltWord, AltTripWord };

        /// <summary>
        /// 名前に書く配置の印。片手ずつと、手が1つだけのとき（配置が効かない）は書かない。
        /// </summary>
        private static string PatternWord(DrillOptions options)
        {
            if (options.Sequences.Count < 2) return "";
            switch (options.HandPattern)
            {
                case HandPattern.Sync: return SyncWord;
                case HandPattern.Alternate: return AltWord;
                case HandPattern.AlternateRoundTrip: return AltTripWord;
                default: return "";
            }
        }

        /// <summary>
        /// タワーのドリルか。名前の <c>BPM</c> より前に <c>tower</c> の語があるかで見る。
        /// </summary>
        public static bool IsTower(string songName)
        {
            return HasWord(songName, TowerWord.Trim());
        }

        /// <summary>名前の <c>BPM</c> より前にその語があるか。</summary>
        private static bool HasWord(string songName, string target)
        {
            if (string.IsNullOrEmpty(songName)) return false;

            foreach (var word in StripId(songName).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word == "BPM") return false;
                if (word == target) return true;
            }
            return false;
        }

        /// <summary>
        /// 旧い名前（<c>Drill R:8&gt;b L:5&gt;a axis 100ms</c>）から遷移と向きの部分を取り出す。
        /// その形でなければ null。
        ///
        /// ID を入れる前に録った記録も同じ行にまとめるために要る。
        /// 旧い名前が持っているのは<b>ノーツ間隔</b>で片手の間隔ではないので、
        /// 片手の間隔はリプレイの実測（軸の整数倍に丸めたもの）から渡す。
        /// </summary>
        public static string LegacyBody(string songName)
        {
            if (string.IsNullOrEmpty(songName)) return null;
            if (!songName.StartsWith(Prefix, StringComparison.Ordinal)) return null;

            string rest = songName.Substring(Prefix.Length).Trim();

            // 昔あった段番号 " #01" は落とす。ドリルとしては同じもの。
            int hash = rest.LastIndexOf('#');
            if (hash > 0) rest = rest.Substring(0, hash).TrimEnd();

            if (!rest.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) return null;

            int end = rest.Length - 2;
            int start = end;
            while (start > 0 && char.IsDigit(rest[start - 1])) start--;
            if (start == end) return null;

            return rest.Substring(0, start).TrimEnd();
        }

        /// <summary>FNV-1a (32bit)。短くて安定していればよいので、暗号強度は要らない。</summary>
        private static uint Fnv1a(string text)
        {
            uint hash = 2166136261;
            var bytes = Encoding.UTF8.GetBytes(text);

            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= 16777619;
            }

            return hash;
        }

        /// <summary>下位ビットから base32 で <see cref="IdLength"/> 文字ぶん取る。</summary>
        private static string Encode(uint value)
        {
            var chars = new char[IdLength];
            for (int i = IdLength - 1; i >= 0; i--)
            {
                chars[i] = Alphabet[(int)(value & 31)];
                value >>= 5;
            }
            return new string(chars);
        }
    }
}
