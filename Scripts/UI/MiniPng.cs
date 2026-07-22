using System;
using System.IO;
using System.IO.Compression;

namespace UsurperRemake.UI
{
    /// <summary>
    /// v0.65.7 NPC portraits: minimal PNG decoder, dependency-free.
    ///
    /// Decodes the narrow PNG profile our portrait pipeline actually produces
    /// (PixelLab API output and our own tooling): 8-bit depth, color types
    /// 0 (gray), 2 (RGB), 3 (palette), 4 (gray+alpha), 6 (RGBA), no interlace.
    /// Anything outside that profile throws InvalidDataException and the
    /// caller falls back to the procedural portrait -- a bad cache file can
    /// never take down a session.
    ///
    /// Deliberately NOT a general-purpose decoder: no Adam7, no 16-bit depth,
    /// no ancillary color management. Adding SixLabors.ImageSharp for one
    /// 34x28 downsample would bloat every BBS/self-contained build; the PNG
    /// spec subset below is ~150 lines and covered by unit tests.
    /// </summary>
    public static class MiniPng
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// Decode PNG bytes to a tightly packed RGBA buffer (4 bytes/pixel).
        /// Throws InvalidDataException on anything outside the supported profile.
        /// </summary>
        public static (byte[] Rgba, int Width, int Height) Decode(byte[] png)
        {
            if (png == null || png.Length < 8 + 25)
                throw new InvalidDataException("PNG too small");
            for (int i = 0; i < 8; i++)
                if (png[i] != Signature[i])
                    throw new InvalidDataException("Bad PNG signature");

            int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
            byte[]? palette = null;
            byte[]? paletteAlpha = null;
            using var idat = new MemoryStream();

            int pos = 8;
            while (pos + 8 <= png.Length)
            {
                int len = ReadInt(png, pos);
                string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                int dataStart = pos + 8;
                // long arithmetic: a crafted len near int.MaxValue would wrap
                // negative in int math and slip past the truncation check.
                if (len < 0 || (long)dataStart + len + 4 > png.Length)
                    throw new InvalidDataException("Truncated PNG chunk");

                switch (type)
                {
                    case "IHDR":
                        if (len < 13)
                            throw new InvalidDataException("Truncated IHDR chunk");
                        width = ReadInt(png, dataStart);
                        height = ReadInt(png, dataStart + 4);
                        bitDepth = png[dataStart + 8];
                        colorType = png[dataStart + 9];
                        interlace = png[dataStart + 12];
                        break;
                    case "PLTE":
                        palette = new byte[len];
                        Array.Copy(png, dataStart, palette, 0, len);
                        break;
                    case "tRNS":
                        paletteAlpha = new byte[len];
                        Array.Copy(png, dataStart, paletteAlpha, 0, len);
                        break;
                    case "IDAT":
                        idat.Write(png, dataStart, len);
                        break;
                    case "IEND":
                        pos = png.Length; // stop scanning
                        continue;
                }
                pos = dataStart + len + 4; // skip CRC
            }

            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                throw new InvalidDataException($"Unsupported PNG dimensions {width}x{height}");
            if (bitDepth != 8)
                throw new InvalidDataException($"Unsupported PNG bit depth {bitDepth}");
            if (interlace != 0)
                throw new InvalidDataException("Interlaced PNG not supported");

            int channels = colorType switch
            {
                0 => 1,  // gray
                2 => 3,  // RGB
                3 => 1,  // palette index
                4 => 2,  // gray + alpha
                6 => 4,  // RGBA
                _ => throw new InvalidDataException($"Unsupported PNG color type {colorType}")
            };
            if (colorType == 3 && palette == null)
                throw new InvalidDataException("Palette PNG missing PLTE");

            // Inflate all IDAT data (zlib-wrapped deflate).
            idat.Position = 0;
            byte[] raw;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                z.CopyTo(outMs);
                raw = outMs.ToArray();
            }

            int stride = width * channels;
            long expected = (long)(stride + 1) * height;
            if (raw.Length < expected)
                throw new InvalidDataException("PNG pixel data truncated");

            // Unfilter scanlines (filters 0-4), then expand to RGBA.
            var rgba = new byte[width * height * 4];
            var prev = new byte[stride];
            var cur = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * (stride + 1);
                byte filter = raw[rowStart];
                Array.Copy(raw, rowStart + 1, cur, 0, stride);

                switch (filter)
                {
                    case 0: break;
                    case 1: // Sub
                        for (int i = channels; i < stride; i++)
                            cur[i] = (byte)(cur[i] + cur[i - channels]);
                        break;
                    case 2: // Up
                        for (int i = 0; i < stride; i++)
                            cur[i] = (byte)(cur[i] + prev[i]);
                        break;
                    case 3: // Average
                        for (int i = 0; i < stride; i++)
                        {
                            int left = i >= channels ? cur[i - channels] : 0;
                            cur[i] = (byte)(cur[i] + ((left + prev[i]) >> 1));
                        }
                        break;
                    case 4: // Paeth
                        for (int i = 0; i < stride; i++)
                        {
                            int a = i >= channels ? cur[i - channels] : 0;
                            int b = prev[i];
                            int c = i >= channels ? prev[i - channels] : 0;
                            cur[i] = (byte)(cur[i] + Paeth(a, b, c));
                        }
                        break;
                    default:
                        throw new InvalidDataException($"Unknown PNG filter {filter}");
                }

                for (int x = 0; x < width; x++)
                {
                    int src = x * channels;
                    int dst = (y * width + x) * 4;
                    switch (colorType)
                    {
                        case 0:
                            rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = cur[src];
                            rgba[dst + 3] = 255;
                            break;
                        case 2:
                            rgba[dst] = cur[src];
                            rgba[dst + 1] = cur[src + 1];
                            rgba[dst + 2] = cur[src + 2];
                            rgba[dst + 3] = 255;
                            break;
                        case 3:
                            int idx = cur[src] * 3;
                            if (idx + 2 >= palette!.Length)
                                throw new InvalidDataException("Palette index out of range");
                            rgba[dst] = palette[idx];
                            rgba[dst + 1] = palette[idx + 1];
                            rgba[dst + 2] = palette[idx + 2];
                            rgba[dst + 3] = paletteAlpha != null && cur[src] < paletteAlpha.Length
                                ? paletteAlpha[cur[src]] : (byte)255;
                            break;
                        case 4:
                            rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = cur[src];
                            rgba[dst + 3] = cur[src + 1];
                            break;
                        case 6:
                            rgba[dst] = cur[src];
                            rgba[dst + 1] = cur[src + 1];
                            rgba[dst + 2] = cur[src + 2];
                            rgba[dst + 3] = cur[src + 3];
                            break;
                    }
                }

                (prev, cur) = (cur, prev);
            }

            return (rgba, width, height);
        }

        private static int ReadInt(byte[] b, int off) =>
            (b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3];

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }
    }
}
