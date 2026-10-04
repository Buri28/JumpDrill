using System;
using System.Collections.Generic;
using System.Globalization;

namespace JumpDrill.Cli
{
    /// <summary>--name value / --flag だけを扱う最小のパーサ。</summary>
    public sealed class CommandLine
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public CommandLine(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (!a.StartsWith("-", StringComparison.Ordinal))
                    throw new FormatException("値だけの引数 '" + a + "' は解釈できません。--オプション名 の形で書いてください。");

                string key = a.TrimStart('-');
                string value = null;

                int eq = key.IndexOf('=');
                if (eq >= 0)
                {
                    value = key.Substring(eq + 1);
                    key = key.Substring(0, eq);
                }
                else if (i + 1 < args.Length && !IsOptionToken(args[i + 1]))
                {
                    value = args[++i];
                }

                _values[key] = value;
                _seen.Add(key);
            }
        }

        /// <summary>負の数を値として受けたいので、数字始まりはオプション扱いしない。</summary>
        private static bool IsOptionToken(string token)
        {
            if (!token.StartsWith("-", StringComparison.Ordinal)) return false;
            if (token.Length < 2) return true;
            char c = token[1];
            return !(char.IsDigit(c) || c == '.');
        }

        public bool Has(params string[] names)
        {
            foreach (var n in names) if (_seen.Contains(n)) return true;
            return false;
        }

        public string GetString(string[] names, string fallback = null)
        {
            foreach (var n in names)
            {
                string v;
                if (_values.TryGetValue(n, out v))
                {
                    if (v == null) throw new FormatException("--" + n + " には値が要ります。");
                    return v;
                }
            }
            return fallback;
        }

        /// <summary>
        /// 値を省略できるオプション用。<c>--zip</c> のように単独でも書けるものは、
        /// 値が無いときに例外ではなく null を返す必要がある。
        /// </summary>
        public string GetOptionalString(params string[] names)
        {
            foreach (var n in names)
            {
                string v;
                if (_values.TryGetValue(n, out v)) return v;
            }
            return null;
        }

        public double? GetDouble(params string[] names)
        {
            var text = GetString(names);
            if (text == null) return null;

            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("--" + names[0] + " の値 '" + text + "' が数値として読めません。");
            return value;
        }

        public int? GetInt(params string[] names)
        {
            var d = GetDouble(names);
            if (!d.HasValue) return null;
            return (int)Math.Round(d.Value);
        }

        /// <summary>知らないオプションを黙って無視しないためのチェック。</summary>
        public IEnumerable<string> UnknownKeys(HashSet<string> known)
        {
            foreach (var key in _seen)
                if (!known.Contains(key)) yield return key;
        }
    }
}
