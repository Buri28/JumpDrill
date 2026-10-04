using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JumpDrill.Serialization
{
    /// <summary>
    /// 依存を増やさないための最小限の JSON 書き出し。
    /// netstandard2.0 のままで MOD 側にも持って行けるようにするため、
    /// System.Text.Json も Newtonsoft も使わない。
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Stack<bool> _hasMembers = new Stack<bool>();
        private readonly bool _indent;
        private int _depth;

        public JsonWriter(bool indent = true)
        {
            _indent = indent;
        }

        public JsonWriter StartObject() { Separator(); _sb.Append('{'); Push(); return this; }
        public JsonWriter EndObject() { Pop(); _sb.Append('}'); return this; }
        public JsonWriter StartArray() { Separator(); _sb.Append('['); Push(); return this; }
        public JsonWriter EndArray() { Pop(); _sb.Append(']'); return this; }

        public JsonWriter Name(string name)
        {
            Separator();
            AppendString(name);
            _sb.Append(':');
            if (_indent) _sb.Append(' ');
            _suppressSeparator = true;
            return this;
        }

        public JsonWriter Value(string value)
        {
            Separator();
            if (value == null) _sb.Append("null"); else AppendString(value);
            return this;
        }

        public JsonWriter Value(bool value) { Separator(); _sb.Append(value ? "true" : "false"); return this; }
        public JsonWriter Value(int value) { Separator(); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }

        public JsonWriter Value(double value)
        {
            Separator();
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("JSON に NaN / Infinity は書けません。", nameof(value));
            _sb.Append(Format(value));
            return this;
        }

        public JsonWriter Property(string name, string value) { return Name(name).Value(value); }
        public JsonWriter Property(string name, int value) { return Name(name).Value(value); }
        public JsonWriter Property(string name, double value) { return Name(name).Value(value); }
        public JsonWriter Property(string name, bool value) { return Name(name).Value(value); }

        public override string ToString() { return _sb.ToString(); }

        /// <summary>指数表記を避けつつ末尾の 0 を落とす。</summary>
        public static string Format(double value)
        {
            if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.############", CultureInfo.InvariantCulture);
        }

        private bool _suppressSeparator;

        private void Push()
        {
            _hasMembers.Push(false);
            _depth++;
            _suppressSeparator = false;
        }

        private void Pop()
        {
            bool had = _hasMembers.Pop();
            _depth--;
            if (_indent && had) NewLine();
            _suppressSeparator = false;
        }

        private void Separator()
        {
            if (_suppressSeparator) { _suppressSeparator = false; return; }
            if (_hasMembers.Count == 0) return;

            bool had = _hasMembers.Pop();
            if (had) _sb.Append(',');
            _hasMembers.Push(true);
            if (_indent) NewLine();
        }

        private void NewLine()
        {
            _sb.Append('\n');
            _sb.Append(' ', _depth * 2);
        }

        private void AppendString(string s)
        {
            _sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7e)
                            _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
