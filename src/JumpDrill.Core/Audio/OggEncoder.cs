using System;
using System.IO;
using OggVorbisEncoder;

namespace JumpDrill.Audio
{
    /// <summary>
    /// Vorbis エンコード。OggVorbisEncoder は libvorbis の純マネージド移植なので
    /// ネイティブ DLL を同梱せずに済み、MOD 側にもそのまま持って行ける。
    /// Beat Saber は .ogg（BeatSaver 配布物の .egg も中身は同じ）を要求する。
    ///
    /// クリック音はモノラルで足りるが、<b>常にステレオで書く</b>。
    /// このライブラリのモノラル出力は品質値によって codebooks が壊れ、
    /// デコードできない ogg ができる（0.2-0.5 と 0.8-0.9 で失敗、1.0 では例外）。
    /// 読めない音源を渡すと Beat Saber はレベルの読み込みに失敗し、
    /// 曲は一覧に出るのに Play ボタンが押せない状態になる。
    /// ステレオは 0.0-1.0 のどこでも通り、無音主体なので容量もほぼ変わらない。
    /// </summary>
    public static class OggEncoder
    {
        /// <summary>クリック音は帯域が狭いので、低めの品質でも十分に輪郭が残る。</summary>
        public const float DefaultQuality = 0.4f;

        /// <summary>
        /// 末尾に足す無音サンプル。エンコーダは最後のブロックを丸ごと吐き切らずに
        /// 終わることがあるので、切り落とされてよい無音を後ろに用意しておく。
        /// </summary>
        private const int TailPadSamples = 8192;

        public static void Write(Stream output, float[] samples, int sampleRate, float quality = DefaultQuality)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (samples == null) throw new ArgumentNullException(nameof(samples));

            const int Channels = 2;
            var info = VorbisInfo.InitVariableBitRate(Channels, sampleRate, quality);
            var serial = new Random().Next();
            var oggStream = new OggStream(serial);

            // 3つのヘッダパケットは独立したページにしないと読めない。
            var comments = new Comments();
            comments.AddTag("ENCODER", "JumpDrill");

            oggStream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
            oggStream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
            oggStream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));

            OggPage page;
            while (oggStream.PageOut(out page, true))
                WritePage(output, page);

            var state = ProcessingState.Create(info);
            const int BlockSize = 1024;
            var block = new float[Channels][];

            int totalSamples = samples.Length + TailPadSamples;
            int written = 0;
            while (written < totalSamples)
            {
                int count = Math.Min(BlockSize, totalSamples - written);
                int fromSource = Math.Max(0, Math.Min(count, samples.Length - written));

                // 左右に同じ波形を入れる。配列は共有せず channel ごとに持つ。
                for (int c = 0; c < Channels; c++)
                {
                    var chunk = new float[count];
                    if (fromSource > 0) Array.Copy(samples, written, chunk, 0, fromSource);
                    block[c] = chunk;
                }

                state.WriteData(block, count, 0);
                written += count;

                FlushPackets(state, oggStream, output, false);
            }

            state.WriteEndOfStream();
            FlushPackets(state, oggStream, output, true);

            while (oggStream.PageOut(out page, true))
                WritePage(output, page);
        }

        public static byte[] ToBytes(float[] samples, int sampleRate, float quality = DefaultQuality)
        {
            using (var ms = new MemoryStream())
            {
                Write(ms, samples, sampleRate, quality);
                return ms.ToArray();
            }
        }

        private static void FlushPackets(ProcessingState state, OggStream oggStream, Stream output, bool force)
        {
            OggPacket packet;
            while (!oggStream.Finished && state.PacketOut(out packet))
            {
                oggStream.PacketIn(packet);

                // 最後のパケット（終わりの印付き）を入れた時点で Finished になるが、そのページはまだ出ていない。
                // ここで止めると最終ページ（EOS フラグ付き）が書かれず、SongCore が曲の長さを読めない
                OggPage page;
                while (oggStream.PageOut(out page, force))
                    WritePage(output, page);
            }
        }

        private static void WritePage(Stream output, OggPage page)
        {
            output.Write(page.Header, 0, page.Header.Length);
            output.Write(page.Body, 0, page.Body.Length);
        }
    }
}
