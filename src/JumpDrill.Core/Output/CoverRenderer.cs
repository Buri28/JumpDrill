using System;
using System.Collections.Generic;
using JumpDrill.Model;

namespace JumpDrill.Output
{
    /// <summary>
    /// カバー画像。曲選択画面でどのドリルか一目で分かるように、
    /// 4x3 グリッドの上に遷移そのものを描く。
    /// </summary>
    public static class CoverRenderer
    {
        /// <summary>無圧縮 PNG なので、曲一覧で見分けが付く最小限に留める。</summary>
        public const int DefaultSize = 128;

        public static byte[] Render(DrillMap map, int size = DefaultSize)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return Render(map.Options.Sequences, size);
        }

        public static byte[] Render(IReadOnlyList<HandSequence> sequences, int size = DefaultSize)
        {
            var canvas = new PixelCanvas(size, size);
            canvas.Fill(0x14, 0x16, 0x1A);

            // グリッドの描画領域。左右に余白を残す。
            int margin = size / 10;
            int cellW = (size - margin * 2) / GridPosition.Columns;
            int cellH = (size - margin * 2) / GridPosition.Rows;
            int originX = (size - cellW * GridPosition.Columns) / 2;
            int originY = (size - cellH * GridPosition.Rows) / 2;

            for (int layer = 0; layer < GridPosition.Rows; layer++)
            {
                for (int index = 0; index < GridPosition.Columns; index++)
                {
                    int x = originX + index * cellW;
                    int y = originY + (GridPosition.Rows - 1 - layer) * cellH;
                    canvas.Rect(x + 2, y + 2, cellW - 4, cellH - 4, 0x2A, 0x2E, 0x36, false);
                }
            }

            foreach (var seq in sequences)
            {
                byte r, g, b;
                if (seq.Hand == Hand.Left) { r = 0xD0; g = 0x3A; b = 0x3A; }
                else { r = 0x2E; g = 0x86; b = 0xD8; }

                // 循環の道筋。末尾から先頭に戻る線も引く。
                for (int i = 0; i < seq.Length; i++)
                {
                    var from = Center(seq.StepAt(i).Position, originX, originY, cellW, cellH);
                    var to = Center(seq.StepAt(i + 1).Position, originX, originY, cellW, cellH);
                    canvas.Line(from.Item1, from.Item2, to.Item1, to.Item2,
                        (byte)(r / 2 + 0x20), (byte)(g / 2 + 0x20), (byte)(b / 2 + 0x20));
                }

                foreach (var step in seq.Steps)
                {
                    var c = Center(step.Position, originX, originY, cellW, cellH);
                    int s = Math.Min(cellW, cellH) / 3;
                    canvas.Rect(c.Item1 - s, c.Item2 - s, s * 2, s * 2, r, g, b, true);
                }
            }

            return PngWriter.Encode(canvas.Pixels, size, size);
        }

        private static Tuple<int, int> Center(GridPosition p, int originX, int originY, int cellW, int cellH)
        {
            return Tuple.Create(
                originX + p.LineIndex * cellW + cellW / 2,
                originY + (GridPosition.Rows - 1 - p.LineLayer) * cellH + cellH / 2);
        }
    }
}
