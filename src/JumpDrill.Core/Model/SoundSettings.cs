using System;
using System.Collections.Generic;
using System.Linq;

namespace JumpDrill.Model
{
    /// <summary>
    /// 生成オプションの窓で選ぶ4項目（クリック音・音色・カウントイン・末尾の余白）。
    /// どれも曲名と ID に入らないので、替えて作り直しても記録はそのまま続く。
    /// </summary>
    public sealed class SoundSettings
    {
        public ClickMode Click { get; set; } = ClickMode.Down;
        public ClickTone Tone { get; set; } = ClickTone.Standard;
        public int CountIn { get; set; } = 8;
        public double TailSeconds { get; set; } = 1.5;

        public static SoundSettings Of(DrillOptions options)
        {
            return new SoundSettings
            {
                Click = options.Click,
                Tone = options.ClickTone,
                CountIn = options.CountInClicks,
                TailSeconds = options.TailSeconds,
            };
        }

        public void ApplyTo(DrillOptions options)
        {
            options.Click = Click;
            options.ClickTone = Tone;
            options.CountInClicks = CountIn;
            options.TailSeconds = TailSeconds;
        }

        public bool SameAs(SoundSettings other)
        {
            return other != null && Click == other.Click && Tone == other.Tone && CountIn == other.CountIn &&
                Math.Abs(TailSeconds - other.TailSeconds) < 1e-9;
        }

        /// <summary>
        /// 何本かのドリルの値を窓の初期値にまとめる。最初の1本の値を使い、どれか違えば <paramref name="differ"/> を立てる。
        /// 1本も無ければ既定値。
        /// </summary>
        public static SoundSettings Common(IEnumerable<SoundSettings> settings, out bool differ)
        {
            var list = settings.Where(s => s != null).ToList();
            differ = list.Skip(1).Any(s => !s.SameAs(list[0]));
            return list.Count == 0 ? new SoundSettings() : list[0];
        }
    }
}
