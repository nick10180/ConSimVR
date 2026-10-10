using System.IO;
using ConSimVR.Core.Graphics;
using Xunit;

namespace ConSimVR.Tests
{
    public class TextureReadbackTests
    {
        [Fact]
        public unsafe void Writes_png_and_summarises_bgra_rows()
        {
            const int width = 64, height = 32, pitch = width * 4 + 16; // padded rows like a mapped texture
            var pixels = new byte[pitch * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * pitch + x * 4;
                    pixels[i] = 255;            // B: blue left half, white right half
                    pixels[i + 1] = (byte)(x < width / 2 ? 0 : 255);
                    pixels[i + 2] = (byte)(x < width / 2 ? 0 : 255);
                    pixels[i + 3] = 255;
                }

            string path = Path.Combine(Path.GetTempPath(), "consimvr-readback-test.png");
            string summary;
            fixed (byte* p = pixels)
                summary = TextureReadback.WriteImage(path, p, pitch, width, height, bgra: true, maxWidth: 32);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
            Assert.Equal(32, bytes[16 + 3]);  // IHDR width (downscaled by 2)
            Assert.Equal(16, bytes[20 + 3]);  // IHDR height
            Assert.Contains("white 50%", summary);
        }
    }
}
