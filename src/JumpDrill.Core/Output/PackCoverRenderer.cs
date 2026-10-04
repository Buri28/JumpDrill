using System;
using JumpDrill.Model;

namespace JumpDrill.Output
{
    /// <summary>
    /// パック一覧に出すカバー画像。
    ///
    /// 個々の譜面カバー（<see cref="CoverRenderer"/>）は遷移そのものを描いて
    /// 曲を見分けるためのものだが、こちらはパックを1つ見分けられればよいので、
    /// グリッドの上に往復の軌跡だけを大きく描く。
    /// 文字は描かない（フォントを持ち込まずに済ませるため）。パック名は
    /// ゲーム側が画像の隣に表示する。
    /// </summary>
    public static class PackCoverRenderer
    {
        public const int DefaultSize = 256;

        public static byte[] Render(int size = DefaultSize)
        {
            var canvas = new PixelCanvas(size, size);
            canvas.Fill(0x11, 0x13, 0x17);

            int cell = size / 5;
            int originX = (size - cell * GridPosition.Columns) / 2;
            int originY = (size - cell * GridPosition.Rows) / 2;

            // 薄いグリッド。何のツールで作ったものかが伝わればよい。
            for (int layer = 0; layer < GridPosition.Rows; layer++)
            {
                for (int index = 0; index < GridPosition.Columns; index++)
                {
                    int x = originX + index * cell;
                    int y = originY + (GridPosition.Rows - 1 - layer) * cell;
                    canvas.Rect(x + 3, y + 3, cell - 6, cell - 6, 0x24, 0x28, 0x30, false);
                }
            }

            // 右手 8>b と左手 5>a。既定のドリルと同じ形。
            DrawStroke(canvas, originX, originY, cell, '8', 'b', 0x2E, 0x86, 0xD8);
            DrawStroke(canvas, originX, originY, cell, '5', 'a', 0xD0, 0x3A, 0x3A);

            return PngWriter.Encode(canvas.Pixels, size, size);
        }

        private static void DrawStroke(PixelCanvas canvas, int originX, int originY, int cell,
                                       char fromToken, char toToken, byte r, byte g, byte b)
        {
            var from = GridPosition.Parse(fromToken);
            var to = GridPosition.Parse(toToken);

            int fx = Center(from.LineIndex, originX, cell);
            int fy = Center(GridPosition.Rows - 1 - from.LineLayer, originY, cell);
            int tx = Center(to.LineIndex, originX, cell);
            int ty = Center(GridPosition.Rows - 1 - to.LineLayer, originY, cell);

            // 線を太らせて縮小表示でも残るようにする。
            for (int offset = -2; offset <= 2; offset++)
                canvas.Line(fx + offset, fy, tx + offset, ty, r, g, b);

            int radius = cell / 3;
            canvas.Rect(tx - radius, ty - radius, radius * 2, radius * 2, r, g, b, true);
            canvas.Rect(fx - radius / 2, fy - radius / 2, radius, radius,
                (byte)(r / 2 + 0x18), (byte)(g / 2 + 0x18), (byte)(b / 2 + 0x18), true);
        }

        private static int Center(int index, int origin, int cell)
        {
            return origin + index * cell + cell / 2;
        }
    }
}
