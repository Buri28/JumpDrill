using System;
using System.IO;
using System.Text;

namespace JumpDrill.Audio
{
    /// <summary>16bit PCM の WAV 書き出し。ogg が使えない環境の逃げ道と検証用。</summary>
    public static class WavWriter
    {
        public static void Write(Stream stream, float[] samples, int sampleRate, int channels = 1)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (samples == null) throw new ArgumentNullException(nameof(samples));

            int frames = samples.Length / channels;
            int dataBytes = frames * channels * 2;

            using (var w = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));

                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);                       // PCM
                w.Write((short)channels);
                w.Write(sampleRate);
                w.Write(sampleRate * channels * 2);      // byte rate
                w.Write((short)(channels * 2));          // block align
                w.Write((short)16);                      // bits per sample

                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);

                for (int i = 0; i < frames * channels; i++)
                {
                    float v = samples[i];
                    if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                    w.Write((short)Math.Round(v * 32767.0));
                }
            }
        }

        public static byte[] ToBytes(float[] samples, int sampleRate, int channels = 1)
        {
            using (var ms = new MemoryStream())
            {
                Write(ms, samples, sampleRate, channels);
                return ms.ToArray();
            }
        }
    }
}
