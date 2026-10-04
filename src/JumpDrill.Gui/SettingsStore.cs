using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace JumpDrill.Gui
{
    /// <summary>
    /// 画面の指定を次の起動まで覚えておく。
    /// 単位を BPM にしていたのに毎回 ms に戻る、といったことを避けるため。
    ///
    /// 形式は key=value の1行1項目。壊れていても既定値で立ち上がればよいので、
    /// パーサを持ち込まずに自前で読み書きする。
    /// </summary>
    internal sealed class SettingsStore
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Path { get; }

        public SettingsStore(string path)
        {
            Path = path;
        }

        public static string DefaultPath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JumpDrill", "settings.txt");
        }

        /// <summary>読めなければ黙って既定値。設定ファイルで起動を止めない。</summary>
        public void Load()
        {
            _values.Clear();
            try
            {
                if (!File.Exists(Path)) return;

                foreach (var line in File.ReadAllLines(Path, Encoding.UTF8))
                {
                    var text = line.Trim();
                    if (text.Length == 0 || text[0] == '#') continue;

                    int eq = text.IndexOf('=');
                    if (eq <= 0) continue;

                    _values[text.Substring(0, eq).Trim()] = text.Substring(eq + 1).Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>保存に失敗しても終了は止めない。</summary>
        public void Save()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("# JumpDrill の画面設定。消せば既定値に戻る。");
                foreach (var pair in _values)
                    sb.Append(pair.Key).Append('=').AppendLine(pair.Value);

                File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Set(string key, string value) { _values[key] = value ?? string.Empty; }
        public void Set(string key, bool value) { _values[key] = value ? "1" : "0"; }
        public void Set(string key, int value) { _values[key] = value.ToString(CultureInfo.InvariantCulture); }

        public void Set(string key, decimal value)
        {
            _values[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 項目を落とす。<see cref="Save"/> は読み込んだ内容をそのまま書き戻すので、
        /// 使わなくなった項目は明示的に消さないとファイルに残り続ける。
        /// </summary>
        public void Remove(string key) { _values.Remove(key); }

        public string GetString(string key, string fallback)
        {
            string value;
            return _values.TryGetValue(key, out value) ? value : fallback;
        }

        public bool GetBool(string key, bool fallback)
        {
            string value;
            if (!_values.TryGetValue(key, out value)) return fallback;
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public int GetInt(string key, int fallback, int min, int max)
        {
            string text;
            int value;
            if (!_values.TryGetValue(key, out text) ||
                !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return fallback;

            return value < min ? min : (value > max ? max : value);
        }

        /// <summary>範囲外の値で NumericUpDown を落とさないよう、必ず丸めて返す。</summary>
        public decimal GetDecimal(string key, decimal fallback, decimal min, decimal max)
        {
            string text;
            decimal value;
            if (!_values.TryGetValue(key, out text) ||
                !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return fallback;

            return value < min ? min : (value > max ? max : value);
        }
    }
}
