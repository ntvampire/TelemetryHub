using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace IconBuilder;

class Program
{
    static void Main(string[] args)
    {
        string srcPath = @"C:\Users\Mike\.gemini\antigravity\brain\43b72ff7-8c3b-4421-9f6a-26b266a35950\telemetry_icon_flat_v2_1790146746073.jpg";
        string targetIco = @"D:\Projects\ksital-telemetry-hub\src\UI.WinUI\Assets\app.ico";
        string previewPng = @"C:\Users\Mike\.gemini\antigravity\brain\43b72ff7-8c3b-4421-9f6a-26b266a35950\scratch\icon_v2_flat_preview.png";

        using var srcImg = new Bitmap(srcPath);
        int w = srcImg.Width;
        int h = srcImg.Height;

        // Create 32-bit ARGB bitmap
        using var argbBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(argbBmp))
        {
            g.DrawImage(srcImg, 0, 0, w, h);
        }

        // BFS flood-fill from edges to make surrounding white background transparent
        bool[,] visited = new bool[w, h];
        Queue<(int x, int y)> queue = new();

        for (int x = 0; x < w; x++)
        {
            queue.Enqueue((x, 0)); visited[x, 0] = true;
            queue.Enqueue((x, h - 1)); visited[x, h - 1] = true;
        }
        for (int y = 0; y < h; y++)
        {
            queue.Enqueue((0, y)); visited[0, y] = true;
            queue.Enqueue((w - 1, y)); visited[w - 1, y] = true;
        }

        // Color threshold for background
        bool IsBackground(Color c) => c.R >= 225 && c.G >= 225 && c.B >= 225;

        List<(int x, int y)> bgPixels = new();

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            Color pixelColor = argbBmp.GetPixel(cx, cy);

            if (IsBackground(pixelColor))
            {
                bgPixels.Add((cx, cy));

                (int dx, int dy)[] dirs = { (-1, 0), (1, 0), (0, -1), (0, 1) };
                foreach (var (dx, dy) in dirs)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h && !visited[nx, ny])
                    {
                        visited[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }

        Console.WriteLine($"Found {bgPixels.Count} background pixels to make transparent.");
        foreach (var (bx, by) in bgPixels)
        {
            argbBmp.SetPixel(bx, by, Color.FromArgb(0, 0, 0, 0));
        }

        // Antialias boundary pixels
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                Color c = argbBmp.GetPixel(x, y);
                if (c.A > 0 && c.R >= 210 && c.G >= 210 && c.B >= 210)
                {
                    // Check if adjacent to transparent pixel
                    bool adjTransparent =
                        argbBmp.GetPixel(x - 1, y).A == 0 ||
                        argbBmp.GetPixel(x + 1, y).A == 0 ||
                        argbBmp.GetPixel(x, y - 1).A == 0 ||
                        argbBmp.GetPixel(x, y + 1).A == 0;

                    if (adjTransparent)
                    {
                        // Calculate smooth alpha based on brightness
                        float brightness = (c.R + c.G + c.B) / 3f;
                        int newA = Math.Clamp((int)((255 - brightness) * 4), 0, 255);
                        argbBmp.SetPixel(x, y, Color.FromArgb(newA, c.R, c.G, c.B));
                    }
                }
            }
        }

        // Find bounding box of visible icon
        int minX = w, maxX = 0, minY = h, maxY = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (argbBmp.GetPixel(x, y).A > 10)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        int iconW = maxX - minX + 1;
        int iconH = maxY - minY + 1;
        Console.WriteLine($"Icon visible bounds: ({minX},{minY}) - {iconW}x{iconH}");

        // Fit into a square with a clean 4% margin
        int iconDim = Math.Max(iconW, iconH);
        int pad = (int)(iconDim * 0.04);
        int finalSize = iconDim + pad * 2;

        using var squareBmp = new Bitmap(finalSize, finalSize, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(squareBmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int dstX = pad + (iconDim - iconW) / 2;
            int dstY = pad + (iconDim - iconH) / 2;
            g.DrawImage(argbBmp, new Rectangle(dstX, dstY, iconW, iconH), new Rectangle(minX, minY, iconW, iconH), GraphicsUnit.Pixel);
        }

        // Save preview PNG
        squareBmp.Save(previewPng, ImageFormat.Png);
        Console.WriteLine($"Saved preview PNG to {previewPng}");

        // Generate multi-resolution ICO (256, 128, 64, 48, 32, 24, 16)
        int[] sizes = { 256, 128, 64, 48, 32, 24, 16 };
        var iconImages = new List<(int size, byte[] data, bool isPng)>();

        foreach (int size in sizes)
        {
            using var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(squareBmp, 0, 0, size, size);
            }

            if (size >= 64)
            {
                // Use PNG for high-res sizes (standard Windows icon spec)
                using var ms = new MemoryStream();
                resized.Save(ms, ImageFormat.Png);
                iconImages.Add((size, ms.ToArray(), true));
            }
            else
            {
                // Use 32bpp DIB for smaller sizes (16, 24, 32, 48) for 100% Win32 / Inno Setup compatibility
                byte[] dibData = CreateDibIconData(resized);
                iconImages.Add((size, dibData, false));
            }
        }

        // Write ICO binary
        using var fs = new FileStream(targetIco, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICO Header
        bw.Write((ushort)0); // Reserved
        bw.Write((ushort)1); // Type 1 = ICO
        bw.Write((ushort)iconImages.Count); // Count

        int dataOffset = 6 + (16 * iconImages.Count);

        // Icon directory entries
        foreach (var img in iconImages)
        {
            bw.Write((byte)(img.size == 256 ? 0 : img.size)); // Width (0 means 256)
            bw.Write((byte)(img.size == 256 ? 0 : img.size)); // Height (0 means 256)
            bw.Write((byte)0); // Color palette
            bw.Write((byte)0); // Reserved
            bw.Write((ushort)1); // Color planes
            bw.Write((ushort)32); // Bits per pixel
            bw.Write((uint)img.data.Length); // Size of image data
            bw.Write((uint)dataOffset); // Offset of image data
            dataOffset += img.data.Length;
        }

        // Icon image data
        foreach (var img in iconImages)
        {
            bw.Write(img.data);
        }

        Console.WriteLine($"Successfully built multi-resolution ICO ({iconImages.Count} sizes) to {targetIco}!");
    }

    static byte[] CreateDibIconData(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        int xorSize = w * h * 4;
        int andRowBytes = ((w + 31) / 32) * 4;
        int andSize = andRowBytes * h;
        int totalSize = 40 + xorSize + andSize;

        byte[] result = new byte[totalSize];
        using var ms = new MemoryStream(result);
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER (40 bytes)
        bw.Write((uint)40); // biSize
        bw.Write((int)w); // biWidth
        bw.Write((int)(h * 2)); // biHeight (XOR + AND mask height)
        bw.Write((ushort)1); // biPlanes
        bw.Write((ushort)32); // biBitCount
        bw.Write((uint)0); // biCompression (BI_RGB)
        bw.Write((uint)(xorSize + andSize)); // biSizeImage
        bw.Write((int)0); // biXPelsPerMeter
        bw.Write((int)0); // biYPelsPerMeter
        bw.Write((uint)0); // biClrUsed
        bw.Write((uint)0); // biClrImportant

        // XOR mask: 32bpp BGRA bottom-up
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = bmp.GetPixel(x, y);
                bw.Write(c.B);
                bw.Write(c.G);
                bw.Write(c.R);
                bw.Write(c.A);
            }
        }

        // AND mask: 1bpp bottom-up (all 0 because 32bpp alpha is used)
        byte[] andRow = new byte[andRowBytes];
        for (int y = 0; y < h; y++)
        {
            bw.Write(andRow);
        }

        return result;
    }
}
