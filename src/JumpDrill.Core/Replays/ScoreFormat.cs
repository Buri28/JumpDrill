using System;
using System.Globalization;

namespace JumpDrill.Replays
{
    /// <summary>
    /// 点数（再現精度 %・精度 %・/100・/115 など）を文字にする。<b>小数は切り捨て。</b>
    /// </summary>
    /// <remarks>
    /// 四捨五入だと、満点に届いていない 99.96 が「100.0」、89.96 が「90.0」と出て、
    /// 満点やメダルの線に届いたように見える（メダルは丸める前の値で決めている）。
    /// 長さ・角度・時間などの測った値は満点が無いので、ここは通さず四捨五入のまま出す。
    /// </remarks>
    public static class ScoreFormat
    {
        /// <summary><paramref name="decimals"/> 桁で切り捨てた文字。</summary>
        public static string Floor(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return value.ToString(CultureInfo.InvariantCulture);

            double scale = Math.Pow(10, decimals);
            // 掛け算の誤差（0.3 × 10 = 2.9999…）で1つ下に落ちないよう、ごく小さい値を足してから切り捨てる
            double floored = Math.Floor(value * scale + 1e-9) / scale;
            return floored.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }
}
