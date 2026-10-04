using System;

namespace JumpDrill.Model
{
    /// <summary>
    /// 4x3 のノーツグリッド上の1マス。記法は設計メモ §2 のとおり:
    /// <code>
    /// 1 2 3 4    lineIndex = (n-1) % 4
    /// 5 6 7 8    lineLayer = 2 - (n-1)/4
    /// 9 a b c
    /// </code>
    /// </summary>
    public struct GridPosition : IEquatable<GridPosition>
    {
        public const int Columns = 4;
        public const int Rows = 3;

        /// <summary>Beat Saber の _lineIndex。0 が左端。</summary>
        public int LineIndex { get; }

        /// <summary>Beat Saber の _lineLayer。0 が最下段。</summary>
        public int LineLayer { get; }

        public GridPosition(int lineIndex, int lineLayer)
        {
            if (lineIndex < 0 || lineIndex >= Columns)
                throw new ArgumentOutOfRangeException(nameof(lineIndex), lineIndex, "lineIndex は 0..3。");
            if (lineLayer < 0 || lineLayer >= Rows)
                throw new ArgumentOutOfRangeException(nameof(lineLayer), lineLayer, "lineLayer は 0..2。");

            LineIndex = lineIndex;
            LineLayer = lineLayer;
        }

        /// <summary>記法の1文字 (1-9, a-c) を座標に。大文字も受ける。</summary>
        public static GridPosition Parse(char token)
        {
            GridPosition result;
            if (!TryParse(token, out result))
                throw new FormatException(Lang.T("グリッド記号 '" + token + "' は 1-9 / a-c ではありません。", "Grid symbol '" + token + "' must be 1-9 / a-c."));
            return result;
        }

        public static bool TryParse(char token, out GridPosition position)
        {
            int n;
            if (token >= '1' && token <= '9') n = token - '0';
            else if (token >= 'a' && token <= 'c') n = token - 'a' + 10;
            else if (token >= 'A' && token <= 'C') n = token - 'A' + 10;
            else { position = default(GridPosition); return false; }

            position = new GridPosition((n - 1) % Columns, 2 - (n - 1) / Columns);
            return true;
        }

        /// <summary>記法の1文字に戻す。</summary>
        public char ToToken()
        {
            int n = (2 - LineLayer) * Columns + LineIndex + 1;
            return n <= 9 ? (char)('0' + n) : (char)('a' + n - 10);
        }

        public bool Equals(GridPosition other)
        {
            return LineIndex == other.LineIndex && LineLayer == other.LineLayer;
        }

        public override bool Equals(object obj)
        {
            return obj is GridPosition && Equals((GridPosition)obj);
        }

        public override int GetHashCode()
        {
            return LineIndex * 31 + LineLayer;
        }

        public override string ToString()
        {
            return ToToken().ToString();
        }

        public static bool operator ==(GridPosition a, GridPosition b) { return a.Equals(b); }
        public static bool operator !=(GridPosition a, GridPosition b) { return !a.Equals(b); }
    }
}
