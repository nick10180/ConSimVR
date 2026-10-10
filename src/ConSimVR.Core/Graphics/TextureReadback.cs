using System;
using System.IO;

namespace ConSimVR.Core.Graphics
{
    /// <summary>
    /// Debug readback of a D3D11 texture: copies it to a CPU-readable staging texture, writes a downscaled 24-bit BMP
    /// and summarises its colours. Uses the immediate context, so call it on the thread that owns it (Unity's render thread).
    /// </summary>
    public static unsafe class TextureReadback
    {
        public static bool TryDump(IntPtr device, IntPtr context, IntPtr texture, string path, int maxWidth, out string summary)
        {
            var desc = D3D11.GetDesc(texture);
            var stagingDesc = desc;
            stagingDesc.MipLevels = 1;
            stagingDesc.ArraySize = 1;
            stagingDesc.Usage = D3D11Usage.Staging;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = D3D11Usage.CpuAccessRead;
            stagingDesc.MiscFlags = 0;
            if (desc.SampleCount > 1)
            {
                summary = $"multisampled ({desc.SampleCount}x), not read";
                return false;
            }

            IntPtr staging = D3D11.CreateTexture2D(device, stagingDesc);
            if (staging == IntPtr.Zero)
            {
                summary = "CreateTexture2D(staging) failed";
                return false;
            }
            try
            {
                D3D11.CopyResource(context, staging, texture);
                byte* data = D3D11.MapRead(context, staging, out uint rowPitch);
                if (data == null)
                {
                    summary = "Map failed";
                    return false;
                }
                try
                {
                    bool bgra = DxgiFormat.Typeless(desc.Format) == DxgiFormat.B8G8R8A8_TYPELESS;
                    summary = WriteImage(path, data, rowPitch, (int)desc.Width, (int)desc.Height, bgra, maxWidth);
                    return true;
                }
                finally
                {
                    D3D11.Unmap(context, staging);
                }
            }
            finally
            {
                D3D11.Release(staging);
            }
        }

        /// <summary>Writes 8-bit RGBA/BGRA pixel rows (downscaled to at most maxWidth) as an RGB PNG; returns a colour summary.</summary>
        public static string WriteImage(string path, byte* data, uint rowPitch, int width, int height, bool bgra, int maxWidth)
        {
            int step = System.Math.Max(1, (width + maxWidth - 1) / System.Math.Max(1, maxWidth));
            int outW = width / step, outH = height / step;
            int stride = 1 + outW * 3; // PNG filter byte + RGB
            var raw = new byte[stride * outH];
            long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
            int white = 0, black = 0;

            for (int y = 0; y < outH; y++)
            {
                byte* row = data + (long)y * step * rowPitch;
                int dst = y * stride + 1;
                for (int x = 0; x < outW; x++)
                {
                    byte* p = row + x * step * 4;
                    byte r = bgra ? p[2] : p[0], g = p[1], b = bgra ? p[0] : p[2], a = p[3];
                    raw[dst + x * 3] = r;
                    raw[dst + x * 3 + 1] = g;
                    raw[dst + x * 3 + 2] = b;
                    sumR += r; sumG += g; sumB += b; sumA += a;
                    if (r > 245 && g > 245 && b > 245) white++;
                    if (r < 10 && g < 10 && b < 10) black++;
                }
            }

            WritePng(Path.ChangeExtension(path, ".png"), outW, outH, raw);
            long n = System.Math.Max(1L, (long)outW * outH);
            return $"{width}x{height} avg RGBA ({sumR / n}, {sumG / n}, {sumB / n}, {sumA / n}), white {100 * white / n}%, black {100 * black / n}%";
        }

        private static void WritePng(string path, int width, int height, byte[] filteredRows)
        {
            using var file = File.Create(path);
            file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 8;  // bit depth
            header[9] = 2;  // colour type RGB
            WriteChunk(file, "IHDR", header);

            using (var compressed = new MemoryStream())
            {
                using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest, true))
                    zlib.Write(filteredRows, 0, filteredRows.Length);
                WriteChunk(file, "IDAT", compressed.ToArray());
            }
            WriteChunk(file, "IEND", Array.Empty<byte>());
        }

        private static void WriteChunk(Stream stream, string type, byte[] payload)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)payload.Length);
            stream.Write(length);
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes);
            stream.Write(payload);
            uint crc = Crc32(Crc32(0xFFFFFFFFu, typeBytes), payload) ^ 0xFFFFFFFFu;
            var crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, crc);
            stream.Write(crcBytes);
        }

        private static readonly uint[] s_crcTable = BuildCrcTable();

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

        private static uint Crc32(uint crc, byte[] bytes)
        {
            foreach (byte b in bytes)
                crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
