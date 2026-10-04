using System;
using System.Collections.Generic;
using System.Linq;
using JumpDrill.Model;

namespace JumpDrill.Output
{
    /// <summary>
    /// 遷移を 4x3 の格子に描いた小さな絵。どの配置のドリルかを一目で見分けるためのもの。
    /// </summary>
    /// <remarks>
    /// GUI のメダル画面の行頭にあるものと同じ絵（<c>MedalForm.DrawIcon</c>）。
    /// あちらは GDI で描いているのでゲーム内では使えず、<see cref="PixelCanvas"/> に描いて
    /// PNG で返す。MOD 側は <c>Texture2D.LoadImage</c> に渡すだけ。
    /// 色は遷移を組む画面（GridPicker）と同じ。
    /// </remarks>
    public static class SequenceIcon
    {
        private static readonly byte[] Background = { 0x14, 0x16, 0x1A };
        private static readonly byte[] GridColor = { 0x4A, 0x50, 0x5C };
        private static readonly byte[] RightColor = { 0x2E, 0x86, 0xD8 };
        private static readonly byte[] LeftColor = { 0xD0, 0x3A, 0x3A };

        /// <summary>格子の外に残す余白 (px)。線の太さぶん。</summary>
        private const int Margin = 2;

        /// <summary>
        /// PNG のバイト列。大きさは格子1マスの画素数で決まる（幅 4 マス × 高さ 3 マス + 余白）。
        /// </summary>
        public static byte[] Render(IReadOnlyList<HandSequence> sequences, int cell = 16)
        {
            if (sequences == null) throw new ArgumentNullException(nameof(sequences));
            if (cell < 4) cell = 4;

            int width = cell * GridPosition.Columns + Margin * 2;
            int height = cell * GridPosition.Rows + Margin * 2;

            var canvas = new PixelCanvas(width, height);
            canvas.Fill(Background[0], Background[1], Background[2]);

            // 格子
            for (int col = 0; col <= GridPosition.Columns; col++)
            {
                double x = Margin + col * cell;
                canvas.LineAA(x, Margin, x, Margin + GridPosition.Rows * cell, GridColor[0], GridColor[1], GridColor[2]);
            }
            for (int row = 0; row <= GridPosition.Rows; row++)
            {
                double y = Margin + row * cell;
                canvas.LineAA(Margin, y, Margin + GridPosition.Columns * cell, y, GridColor[0], GridColor[1], GridColor[2]);
            }

            foreach (var sequence in sequences)
            {
                var color = sequence.Hand == Hand.Right ? RightColor : LeftColor;

                // 横の遷移（85・c9 など）は左右の手が同じマスを通り、線が重なって片方が消える。
                // 右手を少し上、左手を少し下にずらして両方見えるようにする（GUI と同じ）
                double shift = (sequence.Hand == Hand.Right ? -0.18 : 0.18) * cell;

                var points = sequence.Steps.Select(step => new[]
                {
                    Margin + (step.Position.LineIndex + 0.5) * cell,
                    Margin + (GridPosition.Rows - 1 - step.Position.LineLayer + 0.5) * cell + shift,
                }).ToList();

                double thickness = Math.Max(1.5, cell / 8.0);

                if (points.Count == 2)
                {
                    canvas.LineAA(points[0][0], points[0][1], points[1][0], points[1][1],
                                  color[0], color[1], color[2], thickness);
                }
                else if (points.Count > 2)
                {
                    // 3点以上は閉じた循環なので、最後から最初へも結ぶ
                    for (int i = 0; i < points.Count; i++)
                    {
                        var a = points[i];
                        var b = points[(i + 1) % points.Count];
                        canvas.LineAA(a[0], a[1], b[0], b[1], color[0], color[1], color[2], thickness);
                    }
                }

                double radius = cell * 0.28;
                foreach (var point in points)
                    canvas.Disc(point[0], point[1], radius, color[0], color[1], color[2]);
            }

            return PngWriter.Encode(canvas.Pixels, width, height);
        }

        /// <summary>
        /// 譜面名から遷移を読み出す。読めなければ空。
        /// </summary>
        /// <remarks>
        /// 名前は <c>[ID] Drill R8b L5a axis 250 BPM 10s</c> の形（頭に <c>{…}</c> が付くこともある）。
        /// <c>Drill</c> の後ろから、遷移として読める語が続く間だけ拾う。
        /// 一括生成以外のドリル（グリッドで作ったもの）には <see cref="DrillSetEntry"/> が無いので、
        /// 絵を出すには名前から戻すしかない。
        /// </remarks>
        public static List<HandSequence> SequencesFromName(string songName)
        {
            var result = new List<HandSequence>();
            if (string.IsNullOrEmpty(songName)) return result;

            string body = DrillNaming.StripId(songName).Trim();
            if (body.StartsWith(DrillNaming.Prefix, StringComparison.OrdinalIgnoreCase))
                body = body.Substring(DrillNaming.Prefix.Length);

            foreach (var word in body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    result.Add(Parsing.SequenceParser.ParseOne(word));
                }
                catch (FormatException)
                {
                    break;
                }
                catch (ArgumentException)
                {
                    break;
                }
            }

            return result;
        }
    }
}
