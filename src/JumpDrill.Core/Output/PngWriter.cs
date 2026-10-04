using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace JumpDrill.Output
{
    /// <summary>
    /// 無圧縮 (deflate の stored block) の 8bit RGB PNG を書く。
    /// カバー画像1枚のために画像ライブラリを足したくないので自前で持つ。
    /// </summary>
    public static class PngWriter
    {
        public static byte[] Encode(byte[] rgb, int width, int height)
        {
            if (rgb == null) throw new ArgumentNullException(nameof(rgb));
            if (rgb.Length != width * height * 3)
                throw new ArgumentException("rgb の長さが width*height*3 と一致しません。", nameof(rgb));

            // 各行の頭にフィルタ種別バイト (0 = None) を挟む。
            var raw = new byte[height * (1 + width * 3)];
            for (int y = 0; y < height; y++)
            {
                int dst = y * (1 + width * 3);
                raw[dst] = 0;
                Buffer.BlockCopy(rgb, y * width * 3, raw, dst + 1, width * 3);
            }

            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

                var ihdr = new byte[13];
                WriteBigEndian(ihdr, 0, width);
                WriteBigEndian(ihdr, 4, height);
                ihdr[8] = 8;   // bit depth
                ihdr[9] = 2;   // color type: truecolour
                ihdr[10] = 0;  // compression
                ihdr[11] = 0;  // filter
                ihdr[12] = 0;  // interlace
                WriteChunk(ms, "IHDR", ihdr);

                WriteChunk(ms, "IDAT", ZlibStore(raw));
                WriteChunk(ms, "IEND", new byte[0]);

                return ms.ToArray();
            }
        }

        private static byte[] ZlibStore(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); // CMF: deflate, 32K window
                ms.WriteByte(0x01); // FLG: no dictionary, fastest

                int offset = 0;
                do
                {
                    int len = Math.Min(0xFFFF, data.Length - offset);
                    bool last = offset + len >= data.Length;
                    ms.WriteByte((byte)(last ? 1 : 0));
                    ms.WriteByte((byte)(len & 0xFF));
                    ms.WriteByte((byte)((len >> 8) & 0xFF));
                    ms.WriteByte((byte)(~len & 0xFF));
                    ms.WriteByte((byte)((~len >> 8) & 0xFF));
                    ms.Write(data, offset, len);
                    offset += len;
                } while (offset < data.Length);

                uint adler = Adler32(data);
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);

                return ms.ToArray();
            }
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var header = new byte[4];
            WriteBigEndian(header, 0, data.Length);
            stream.Write(header, 0, 4);

            var typeBytes = Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes, 0, 4);
            stream.Write(data, 0, data.Length);

            uint crc = Crc32(typeBytes, data);
            var crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, unchecked((int)crc));
            stream.Write(crcBytes, 0, 4);
        }

        private static void WriteBigEndian(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(params byte[][] parts)
        {
            uint c = 0xFFFFFFFFu;
            foreach (var part in parts)
                foreach (byte b in part)
                    c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
