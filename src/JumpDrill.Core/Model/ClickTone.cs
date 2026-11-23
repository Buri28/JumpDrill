using System;

namespace JumpDrill.Model
{
    /// <summary>クリックの音色。粒度とは独立で、既存の譜面は Standard。</summary>
    public enum ClickTone
    {
        Standard = 0,
        Low = 1,
        /// <summary>音程が下がる重いキック。</summary>
        Kick = 2,
        /// <summary>短い矩形波。</summary>
        Retro = 3,
        /// <summary>ノコギリ波をローパスでこもらせたベース。</summary>
        SynthBass = 4,
        // 5 は以前の KickBass。シンセベースと区別がつかないので消した。読むときは SynthBass にする。
    }

    public static class ClickTones
    {
        /// <summary>以前あった音色の名前。読むときは <see cref="ClickTone.SynthBass"/> にする。</summary>
        public const string RemovedKickBass = "KickBass";
        private const int RemovedKickBassValue = 5;

        /// <summary>
        /// 保存された音色の名前（または数字）を読む。消した KickBass はシンセベースとして読む。
        /// 知らない名前は false。
        /// </summary>
        public static bool TryParse(string text, out ClickTone tone)
        {
            tone = ClickTone.Standard;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (string.Equals(text, RemovedKickBass, StringComparison.OrdinalIgnoreCase))
            {
                tone = ClickTone.SynthBass;
                return true;
            }
            // Enum.TryParse は "Low, Kick" をビットの OR で読むので、名前は1つだけにする
            if (text.IndexOf(',') >= 0) return false;
            // 数字の文字列（"123"）でも TryParse は通るので、定義された値かも見る
            if (!Enum.TryParse(text, true, out ClickTone parsed)) return false;
            if ((int)parsed == RemovedKickBassValue) parsed = ClickTone.SynthBass;
            if (!Enum.IsDefined(typeof(ClickTone), parsed)) return false;
            tone = parsed;
            return true;
        }

        /// <summary>
        /// GUI の設定に保存したプルダウンの番号（＝enum の値）を読む。消した KickBass（5）はシンセベース、
        /// 知らない番号は標準。
        /// </summary>
        public static ClickTone FromIndex(int index)
        {
            if (index == RemovedKickBassValue) return ClickTone.SynthBass;
            return Enum.IsDefined(typeof(ClickTone), index) ? (ClickTone)index : ClickTone.Standard;
        }

        public static ClickTone Parse(string text, ClickTone fallback)
        {
            return TryParse(text, out var tone) ? tone : fallback;
        }
    }
}
