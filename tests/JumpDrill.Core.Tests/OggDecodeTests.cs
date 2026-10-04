using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JumpDrill.Audio;
using JumpDrill.Generation;
using JumpDrill.Model;
using JumpDrill.Output;
using JumpDrill.Parsing;
using NVorbis;
using Xunit;

namespace JumpDrill.Tests
{
    /// <summary>
    /// 書き出した ogg を実際にデコードして確かめる。
    ///
    /// 構造（OggS のページ並びやグラニュル位置）だけを見ていたときは
    /// 壊れた ogg を正常と誤判定していた。中身が読めない音源を渡すと
    /// Beat Saber はレベルの読み込みに失敗し、曲は一覧に出るのに
    /// Play ボタンが押せないという分かりにくい壊れ方をする。
    /// </summary>
    public class OggDecodeTests
    {
        private const int SampleRate = 44100;

        private static string WriteTempOgg(float[] samples, float quality = OggEncoder.DefaultQuality)
        {
            string path = Path.Combine(Path.GetTempPath(), "JumpDrillOgg_" + Guid.NewGuid().ToString("N") + ".ogg");
            File.WriteAllBytes(path, OggEncoder.ToBytes(samples, SampleRate, quality));
            return path;
        }

        private sealed class Decoded
        {
            public int Channels;
            public int SampleRate;
            public long Samples;
            public double Peak;
            public List<double> BurstTimes = new List<double>();
        }

        private static Decoded Decode(string path)
        {
            var result = new Decoded();
            using (var reader = new VorbisReader(path))
            {
                result.Channels = reader.Channels;
                result.SampleRate = reader.SampleRate;

                var buffer = new float[reader.Channels * 4096];
                long frames = 0;
                int n;

                // 正弦波は1周期ごとに0を横切るので、サンプル単位で見ると
                // 1発のクリックが何十回も鳴ったように数えられてしまう。
                // 窓ごとのピークを取ってから立ち上がりを探す。
                const int Window = 512;
                var envelope = new List<double>();
                double windowPeak = 0;
                int inWindow = 0;

                while ((n = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < n; i += reader.Channels)
                    {
                        double a = Math.Abs(buffer[i]);
                        if (a > result.Peak) result.Peak = a;
                        if (a > windowPeak) windowPeak = a;

                        if (++inWindow == Window)
                        {
                            envelope.Add(windowPeak);
                            windowPeak = 0;
                            inWindow = 0;
                        }
                    }
                    frames += n / reader.Channels;
                }
                if (inWindow > 0) envelope.Add(windowPeak);

                bool inBurst = false;
                for (int w = 0; w < envelope.Count; w++)
                {
                    if (envelope[w] > 0.15)
                    {
                        if (!inBurst)
                            result.BurstTimes.Add((double)w * Window / reader.SampleRate);
                        inBurst = true;
                    }
                    else if (envelope[w] < 0.05)
                    {
                        inBurst = false;
                    }
                }

                result.Samples = frames;
            }
            return result;
        }

        /// <summary>
        /// 最後のページに終わりの印（EOS フラグ）が付き、そのグラニュル位置が曲の長さになっている。
        /// SongCore は印の付いたページを末尾から探して曲の長さを読む。無いと曲ごとに警告を出す。
        /// </summary>
        [Fact]
        public void LastPageIsMarkedEndOfStream()
        {
            var samples = new float[SampleRate * 3];
            byte[] bytes = OggEncoder.ToBytes(samples, SampleRate);

            // ページを頭から順にたどる（ヘッダ 27 バイト + セグメント表 + 本体）。
            // 中身を "OggS" で探すと、音声データに偶然出てきた並びを拾うことがある
            int last = -1;
            int pos = 0;
            while (pos + 27 <= bytes.Length)
            {
                Assert.Equal("OggS", System.Text.Encoding.ASCII.GetString(bytes, pos, 4));
                int segments = bytes[pos + 26];
                int body = 0;
                for (int i = 0; i < segments; i++) body += bytes[pos + 27 + i];
                last = pos;
                pos += 27 + segments + body;
            }

            Assert.Equal(bytes.Length, pos);
            Assert.Equal(4, bytes[last + 5] & 4);
            Assert.True(BitConverter.ToInt64(bytes, last + 6) >= samples.Length);
        }

        [Fact]
        public void Encoded_audio_decodes()
        {
            var samples = ClickTrackRenderer.Render(
                new[] { new ClickEvent(0.5, false), new ClickEvent(1.0, true) }, 2.0, SampleRate,
                ClickVoice.Normal, ClickVoice.Accent);

            string path = WriteTempOgg(samples);
            try
            {
                var decoded = Decode(path);

                Assert.Equal(SampleRate, decoded.SampleRate);
                Assert.True(decoded.Samples >= samples.Length,
                    "末尾が切られている: " + decoded.Samples + " < " + samples.Length);
                Assert.True(decoded.Peak > 0.3, "音が入っていない (peak " + decoded.Peak + ")");
            }
            finally { File.Delete(path); }
        }

        [Theory]
        // このライブラリはモノラルだと品質値によって codebooks が壊れるので
        // ステレオで書いている。どの品質でも通ることを固定しておく。
        [InlineData(0.0f)]
        [InlineData(0.2f)]
        [InlineData(0.4f)]
        [InlineData(0.6f)]
        [InlineData(0.9f)]
        public void Every_quality_setting_produces_a_decodable_stream(float quality)
        {
            var samples = ClickTrackRenderer.Render(
                new[] { new ClickEvent(0.3, false), new ClickEvent(0.8, true) }, 1.5, SampleRate,
                ClickVoice.Normal, ClickVoice.Accent);

            string path = WriteTempOgg(samples, quality);
            try
            {
                var decoded = Decode(path);
                Assert.True(decoded.Samples > 0);
                Assert.True(decoded.Peak > 0.3, "quality " + quality + " で音が壊れている (peak " + decoded.Peak + ")");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Amplitude_survives_the_round_trip()
        {
            // モノラルで書いていたときは 0.7 の入力が 0.32 まで潰れていた。
            var samples = new float[SampleRate];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (float)(0.7 * Math.Sin(2 * Math.PI * 1000 * i / SampleRate));

            string path = WriteTempOgg(samples);
            try
            {
                var decoded = Decode(path);
                Assert.InRange(decoded.Peak, 0.5, 1.0);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Clicks_land_where_the_map_puts_them()
        {
            var clicks = new[]
            {
                new ClickEvent(0.50, false),
                new ClickEvent(1.00, false),
                new ClickEvent(1.50, true),
            };
            var samples = ClickTrackRenderer.Render(clicks, 2.0, SampleRate, ClickVoice.Normal, ClickVoice.Accent);

            string path = WriteTempOgg(samples);
            try
            {
                var decoded = Decode(path);

                Assert.Equal(clicks.Length, decoded.BurstTimes.Count);
                for (int i = 0; i < clicks.Length; i++)
                    Assert.Equal(clicks[i].TimeSeconds, decoded.BurstTimes[i], 1);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void A_written_level_carries_playable_audio()
        {
            string root = Path.Combine(Path.GetTempPath(), "JumpDrillOggLevel", Guid.NewGuid().ToString("N"));
            try
            {
                var map = DrillGenerator.Generate(new DrillOptions
                {
                    Sequences = SequenceParser.ParseAll("R:8>b"),
                    IntervalMs = 300,
                    DurationSeconds = 5,
                });

                string folder = LevelWriter.Write(map, root);
                var decoded = Decode(Path.Combine(folder, "song.ogg"));

                // 音源が譜面より短いと最後のノーツが鳴る前に曲が終わる。
                double seconds = decoded.Samples / (double)decoded.SampleRate;
                Assert.True(seconds >= map.TotalSeconds - 0.01,
                    "音源が譜面より短い: " + seconds + " < " + map.TotalSeconds);
                Assert.True(decoded.Peak > 0.3);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
