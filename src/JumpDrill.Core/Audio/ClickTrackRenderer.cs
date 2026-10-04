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
            return Render(map.Clicks, map.TotalSeconds, sampleRate, ClickVoice.Normal, ClickVoice.Accent);
        }

        public static float[] Render(
            IReadOnlyList<ClickEvent> clicks,
            double totalSeconds,
            int sampleRate,
            ClickVoice normal,
            ClickVoice accent)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

            int length = (int)Math.Ceiling(totalSeconds * sampleRate);
            if (length < 1) length = 1;
            var buffer = new float[length];

            if (clicks != null)
            {
                foreach (var click in clicks)
                {
                    var voice = click.Accent ? accent : normal;
                    Mix(buffer, sampleRate, click.TimeSeconds, voice);
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
