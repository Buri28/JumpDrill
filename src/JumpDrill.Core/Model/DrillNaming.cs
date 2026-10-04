using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JumpDrill.Model
{
    /// <summary>
    /// 譜面名の組み立てと読み戻し。CLI と GUI で同じ名前になるよう、ここ1か所で作る。
    ///
    /// <code>
    ///   [Q7K3M] Drill R8b L5a axis 300 BPM 30s
    ///   [Q7K3M] Drill R8b L5a axis 300 BPM 30s x3      セットを繰り返すとき
    ///   {R8bL5a 300} [Q7K3M] Drill R8b L5a axis 300 BPM 10s   一括生成の1本
    /// </code>
    ///
    /// 速さは<b>片手の速さ</b>を BPM（1/2）で書く。<c>30000 / 片手の間隔(ms)</c>。
    /// ScoreSaber の「350 BPM」と同じ数え方で、ジャンプ（同時・片手ずつ）なら
    /// GUI で入れる BPM とも同じ値になる。両手交互だけは自分の番が半分なので、
    /// 入れた BPM より小さく出る。以前は同じ値を「EBPM」と書いていた。
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
        /// 片手の間隔 (ms)。<b>ノーツ間隔とは限らない。</b>
        ///
        /// 両手交互（<c>alt</c>）では自分の番が手の数ぶんおきにしか回ってこないので、
        /// 片手の間隔はノーツ間隔の手数倍になる。同時・片手ずつはノーツ間隔と同じ。
        /// </summary>
        public static double HandIntervalMs(DrillOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            return options.HandPattern == HandPattern.Alternate
                ? options.IntervalMs * options.Sequences.Count
                : options.IntervalMs;
        }

        /// <summary>片手の速さ（BPM 1/2 で数えた値、いわゆる EBPM）。譜面名に書く値。</summary>
        public static double Ebpm(DrillOptions options)
        {
            return Tempo.EbpmFromHandIntervalMs(HandIntervalMs(options));
        }

        /// <summary>
        /// 譜面名を組み立てる。
        /// </summary>
        /// <param name="explicitBody">遷移と向きの代わりに使う文字列。<c>--name</c> 指定。</param>
        public static string Compose(DrillOptions options, string explicitBody = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var ci = CultureInfo.InvariantCulture;
            string body = explicitBody ?? string.Format(ci, "{0} {1}",
                string.Join(" ", options.Sequences.Select(s => s.ToString()).ToArray()),
                options.Direction.ToString().ToLowerInvariant());

            // 速さは 1 桁だけ小数を残す。整数に丸めると 171ms が 175 BPM = 171.4ms に戻り、
            // 「片手 171 ms」と出したいところが 171.4 ms になる。
            string speed = string.Format(ci, "{0:0.#} BPM", Ebpm(options));
            string length = Length(options.DurationSeconds, options.Sets);

            return string.Format(ci, "[{0}] {1}{2} {3} {4}",
                MakeId(body, HandIntervalMs(options), options.DurationSeconds, options.Sets),
                Prefix, body, speed, length);
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
        /// ID（と並び順の印）を落とした表示用の名前。ID が無ければそのまま返す。
        /// </summary>
        public static string StripId(string songName)
        {
            if (ExtractId(songName) == null) return songName;
            return songName.Substring(AfterSetTag(songName) + IdLength + 2).TrimStart();
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
