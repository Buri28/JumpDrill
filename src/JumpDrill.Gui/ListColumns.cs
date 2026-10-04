using System;
using System.Windows.Forms;

namespace JumpDrill.Gui
{
    /// <summary>ListView の列幅合わせ。譜面・プレイ・内訳で同じ合わせ方をするので共通にする。</summary>
    internal static class ListColumns
    {
        /// <summary>
        /// 列幅を中身に合わせる。見出しの方が長い列（「中心ずれ cm」など）があるので、
        /// 見出しと中身の広い方を採る。決め打ちの幅だとフォントや DPI で切れる。
        ///
        /// 見出し幅に <c>Width = -2</c> は使えない。Win32 の仕様で、
        /// <b>最終列だけは残り幅いっぱいに広がる</b>ので、そこだけ伸びて横スクロールが出る。
        /// </summary>
        public static void FitAll(ListView list, int firstFitted)
        {
            list.BeginUpdate();
            for (int i = firstFitted; i < list.Columns.Count; i++) Fit(list, list.Columns[i]);
            FillLast(list);
            list.EndUpdate();
        }

        /// <summary>伸ばす前の、中身に合わせた幅の合計。窓の大きさはこれで決める。</summary>
        public static int NaturalWidth(ListView list)
        {
            int width = 0;
            foreach (ColumnHeader column in list.Columns)
                width += column.Tag is int ? (int)column.Tag : column.Width;

            return width;
        }

        /// <summary>
        /// 余った幅は最後の列にあげる。表の幅が変わるたびに呼ぶ。
        ///
        /// 列の合計が表より狭いと、見出しの帯が途中で終わって右端が地のまま残る。
        /// 見出しを自前で描いている（<see cref="Theme"/>）ので、そこだけ明るい色の
        /// 四角として浮く。
        ///
        /// 足すのは<b>控えてある自然幅から</b>。いまの幅に足し続けると、
        /// 呼ぶたびに最後の列が伸びていく。
        /// </summary>
        public static void FillLast(ListView list)
        {
            if (list.Columns.Count == 0) return;

            var last = list.Columns[list.Columns.Count - 1];
            int natural = last.Tag is int ? (int)last.Tag : last.Width;

            int used = 0;
            for (int i = 0; i < list.Columns.Count - 1; i++) used += list.Columns[i].Width;

            int room = list.ClientSize.Width - used - natural;
            last.Width = room > 0 ? natural + room : natural;
        }

        /// <summary>
        /// 列ごとの余白 (px)。
        ///
        /// 14 px 取っていたが、列が14本ある表では余白だけで 200 px になり、
        /// それだけで横スクロールが出ていた。読みやすさは隣の列と離れていれば足りる。
        /// </summary>
        private const int Slack = 9;

        public static void Fit(ListView list, ColumnHeader column)
        {
            column.Width = -1;              // 中身に合わせる
            int content = column.Width;
            int header = TextRenderer.MeasureText(column.Text, list.Font).Width;

            column.Width = Math.Max(content, header) + Slack;

            // 伸ばす前の幅を控えておく。窓の大きさはこちらで決める
            // （伸ばしたあとの幅で測ると、測るたびに窓が広がっていく）。
            column.Tag = column.Width;
        }
    }
}
