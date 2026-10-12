using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JumpDrill.Serialization
{
    /// <summary>
    /// 依存を増やさないための最小限の JSON 読み込み（<see cref="JsonWriter"/> の対）。
    ///
    /// 戻り値は object が <see cref="Dictionary{TKey,TValue}"/>（string → object）、
    /// 配列が <see cref="List{T}"/>（object）、数は double、ほかは string / bool / null。
    /// 型を当てはめる仕組みは持たない。読む側が辞書から拾う。
    /// </summary>
    public static class JsonReader
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            int pos = 0;
            object value = ReadValue(text, ref pos);
            SkipSpace(text, ref pos);
            if (pos < text.Length) throw Error(pos, "余分な文字があります");
            return value;
        }

        private static object ReadValue(string s, ref int pos)
        {
            SkipSpace(s, ref pos);
            if (pos >= s.Length) throw Error(pos, "値がありません");

            char c = s[pos];
            if (c == '{') return ReadObject(s, ref pos);
            if (c == '[') return ReadArray(s, ref pos);
            if (c == '"') return ReadString(s, ref pos);
            if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(s, ref pos);
            if (Match(s, ref pos, "true")) return true;
            if (Match(s, ref pos, "false")) return false;
            if (Match(s, ref pos, "null")) return null;
            throw Error(pos, "読めない値です");
        }

        private static Dictionary<string, object> ReadObject(string s, ref int pos)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            pos++;
            SkipSpace(s, ref pos);
            if (pos < s.Length && s[pos] == '}') { pos++; return result; }

            while (true)
            {
                SkipSpace(s, ref pos);
                if (pos >= s.Length || s[pos] != '"') throw Error(pos, "名前がありません");
                string name = ReadString(s, ref pos);

                SkipSpace(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') throw Error(pos, "':' がありません");
                pos++;

                result[name] = ReadValue(s, ref pos);

                SkipSpace(s, ref pos);
                if (pos >= s.Length) throw Error(pos, "'}' がありません");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return result; }
                throw Error(pos, "',' か '}' がありません");
            }
        }

        private static List<object> ReadArray(string s, ref int pos)
        {
            var result = new List<object>();
            pos++;
            SkipSpace(s, ref pos);
            if (pos < s.Length && s[pos] == ']') { pos++; return result; }

            while (true)
            {
                result.Add(ReadValue(s, ref pos));

                SkipSpace(s, ref pos);
                if (pos >= s.Length) throw Error(pos, "']' がありません");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return result; }
                throw Error(pos, "',' か ']' がありません");
            }
        }

        private static string ReadString(string s, ref int pos)
        {
            var sb = new StringBuilder();
            pos++;

            while (pos < s.Length)
            {
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }

                if (pos >= s.Length) break;
                char e = s[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > s.Length) throw Error(pos, "\\u の後ろが足りません");
                        sb.Append((char)int.Parse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        pos += 4;
                        break;
                    default: throw Error(pos, "読めないエスケープです");
                }
            }

            throw Error(pos, "文字列が閉じていません");
        }

        private static double ReadNumber(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && "+-0123456789.eE".IndexOf(s[pos]) >= 0) pos++;

            double value;
            if (!double.TryParse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw Error(start, "数として読めません");
            return value;
        }

        private static bool Match(string s, ref int pos, string word)
        {
            if (string.CompareOrdinal(s, pos, word, 0, word.Length) != 0) return false;
            pos += word.Length;
            return true;
        }

        private static void SkipSpace(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        private static FormatException Error(int pos, string message)
        {
            return new FormatException(string.Format(CultureInfo.InvariantCulture, "JSON: {0}（{1} 文字目）", message, pos + 1));
        }
    }
}
