using System;
using System.Collections.Generic;
using JumpDrill.Model;

namespace JumpDrill.Audio
{
    /// <summary>
    /// クリックを音源そのものに焼き込む（設計メモ §3）。
    /// Beat Saber のノーツタイミングは音源の再生位置に紐づくので、
    /// 焼き込めばノーツとクリックは原理的にズレない。
    /// beat 0 = サンプル 0。
    /// </summary>
    public static class ClickTrackRenderer
    {
        public const int DefaultSampleRate = 44100;

        public static float[] Render(DrillMap map, int sampleRate = DefaultSampleRate)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var tone = map.Options.ClickTone;
            return Render(map.Clicks, map.TotalSeconds, sampleRate,
                ClickVoice.For(tone, false), ClickVoice.For(tone, true), ClickVoice.CountIn, ClickVoice.Weak(tone));
        }

        /// <summary>カウントインも accent で鳴らす（音色を分けないとき）。</summary>
        public static float[] Render(
            IReadOnlyList<ClickEvent> clicks,
            double totalSeconds,
            int sampleRate,
            ClickVoice normal,
            ClickVoice accent)
        {
            return Render(clicks, totalSeconds, sampleRate, normal, accent, accent);
        }

        /// <summary>カウントインは countIn の音で鳴らす。クリックの音色と聞き分けられるようにするため。</summary>
        public static float[] Render(
            IReadOnlyList<ClickEvent> clicks,
            double totalSeconds,
            int sampleRate,
            ClickVoice normal,
            ClickVoice accent,
            ClickVoice countIn)
        {
            return Render(clicks, totalSeconds, sampleRate, normal, accent, countIn, ClickVoice.WeakOf(normal));
        }

        /// <summary>弱いクリック（粒度 Beat の拍の間）は weak の音で鳴らす。</summary>
        public static float[] Render(
            IReadOnlyList<ClickEvent> clicks,
            double totalSeconds,
            int sampleRate,
            ClickVoice normal,
            ClickVoice accent,
            ClickVoice countIn,
            ClickVoice weak)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

            int length = (int)Math.Ceiling(totalSeconds * sampleRate);
            if (length < 1) length = 1;
            var buffer = new float[length];

            if (clicks != null)
            {
                // 正弦波以外は1発分を先に作っておき、クリックごとに足す
                float[] normalWave = normal.Shape == ClickShape.Sine ? null : normal.Synthesize(sampleRate);
                float[] accentWave = accent.Shape == ClickShape.Sine ? null : accent.Synthesize(sampleRate);
                float[] countInWave = countIn.Shape == ClickShape.Sine ? null : countIn.Synthesize(sampleRate);
                float[] weakWave = weak.Shape == ClickShape.Sine ? null : weak.Synthesize(sampleRate);
                // 長い音は、次のクリックの手前で消して重ならないようにする（速い譜面で割れたり拍がぼやけたりしない）
                var starts = new List<int>();
                foreach (var click in clicks) starts.Add((int)Math.Round(click.TimeSeconds * sampleRate));
                var sorted = new List<int>(starts);
                sorted.Sort();
                for (int c = 0; c < clicks.Count; c++)
                {
                    var click = clicks[c];
                    var voice = click.CountIn ? countIn : click.Accent ? accent : click.Weak ? weak : normal;
                    var wave = click.CountIn ? countInWave : click.Accent ? accentWave : click.Weak ? weakWave : normalWave;
                    if (wave != null) MixWave(buffer, sampleRate, starts[c], wave, NextStart(sorted, starts[c]) - starts[c]);
                    else Mix(buffer, sampleRate, click.TimeSeconds, voice);
                }
            }

            // 焼き込みの重なりで振り切れないように最後だけ抑える。
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] > 1f) buffer[i] = 1f;
                else if (buffer[i] < -1f) buffer[i] = -1f;
            }

            return buffer;
        }

        /// <summary>start より後の最初のクリックの位置。無ければ int.MaxValue。</summary>
        private static int NextStart(List<int> sorted, int start)
        {
            int i = sorted.BinarySearch(start + 1);
            if (i < 0) i = ~i;
            return i < sorted.Count ? sorted[i] : int.MaxValue;
        }

        /// <summary>1発分を足す。room（次のクリックまでのサンプル数）より長ければ、その手前 3ms で消す。</summary>
        private static void MixWave(float[] buffer, int sampleRate, int start, float[] wave, long room)
        {
            int count = (int)Math.Min(wave.Length, Math.Max(0, room));
            int fade = count < wave.Length ? Math.Max(1, Math.Min(count, (int)Math.Round(0.003 * sampleRate))) : 0;
            for (int i = 0; i < count; i++)
            {
                int index = start + i;
                if (index < 0) continue;
                if (index >= buffer.Length) break;
                float g = 1f;
                if (fade > 0 && i >= count - fade) g = (float)(count - 1 - i) / fade;
                buffer[index] += wave[i] * g;
            }
        }

        private static void Mix(float[] buffer, int sampleRate, double startSeconds, ClickVoice voice)
        {
            int start = (int)Math.Round(startSeconds * sampleRate);
            if (start >= buffer.Length) return;

            int count = (int)Math.Round(voice.DurationSeconds * sampleRate);
            int attack = Math.Max(1, (int)Math.Round(voice.AttackSeconds * sampleRate));
            // 減衰は持続長で約 -52dB まで落ちるように。末尾でぶつ切りにならない。
            double tau = voice.DurationSeconds / 6.0;
            double omega = 2.0 * Math.PI * voice.FrequencyHz / sampleRate;

            for (int i = 0; i < count; i++)
            {
                int index = start + i;
                if (index < 0) continue;
                if (index >= buffer.Length) break;

                double t = (double)i / sampleRate;
                double env = i < attack ? (double)i / attack : Math.Exp(-(t - voice.AttackSeconds) / tau);
                buffer[index] += (float)(voice.Amplitude * env * Math.Sin(omega * i));
            }
        }
    }
}
