using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace EqWikiOverlay.Core;

/// <summary>One OCR'd line with its bounding box (in captured-image pixel space).</summary>
public sealed record OcrLine(string Text, double Top, double Height, double Left);

/// <summary>OCR over a captured bitmap using the built-in Windows.Media.Ocr engine.</summary>
public sealed class OcrService
{
    private readonly OcrEngine? _engine;

    public bool Available => _engine is not null;

    public OcrService()
    {
        // Prefer the user's profile languages; fall back to en-US.
        _engine = OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
    }

    /// <summary>
    /// Runs OCR on the bitmap. Upscales small images first — EQ tooltip text is tiny and OCR
    /// accuracy improves substantially at 2-3x. Returns lines top-to-bottom.
    /// </summary>
    public async Task<IReadOnlyList<OcrLine>> ReadLinesAsync(Bitmap bmp)
    {
        if (_engine is null)
            return Array.Empty<OcrLine>();

        using var scaled = Upscale(bmp, TargetScale(bmp));
        using var software = ToSoftwareBitmap(scaled);
        var result = await _engine.RecognizeAsync(software);

        return result.Lines
            .Select(l => new OcrLine(
                Text: string.Join(" ", l.Words.Select(w => w.Text)),
                Top: l.Words.Count > 0 ? l.Words.Min(w => w.BoundingRect.Top) : 0,
                Height: l.Words.Count > 0 ? l.Words.Max(w => w.BoundingRect.Height) : 0,
                Left: l.Words.Count > 0 ? l.Words.Min(w => w.BoundingRect.Left) : 0))
            .OrderBy(l => l.Top)
            .ToList();
    }

    /// <summary>Convenience: the full OCR'd text as one string.</summary>
    public async Task<string> ReadTextAsync(Bitmap bmp) =>
        string.Join("\n", (await ReadLinesAsync(bmp)).Select(l => l.Text));

    private static int TargetScale(Bitmap bmp)
    {
        // EQ tooltip glyphs are ~13-15px tall; OCR is far more accurate at 3-4x. Scale by the
        // capture height toward ~1300px, capped at 4x.
        if (bmp.Height <= 0) return 1;
        int s = Math.Max(2, (int)Math.Round(1300.0 / bmp.Height));
        return Math.Min(s, 4);
    }

    private static Bitmap Upscale(Bitmap src, int scale)
    {
        var dst = new Bitmap(Math.Max(1, src.Width * scale), Math.Max(1, src.Height * scale),
            PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(src, 0, 0, dst.Width, dst.Height);
        }
        BoostContrast(dst);
        return dst;
    }

    /// <summary>
    /// Simple contrast stretch to sharpen antialiased tooltip text: pushes light text lighter and
    /// dark background darker, which reduces OCR letter confusions (e.g. "ti" read as "b").
    /// </summary>
    private static void BoostContrast(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = data.Stride * data.Height;
            var buf = new byte[bytes];
            Marshal.Copy(data.Scan0, buf, 0, bytes);

            // Per-channel: v' = clamp(((v/255 - 0.5) * gain + 0.5) * 255)
            const double gain = 1.7;
            for (int i = 0; i < bytes; i += 4) // BGRA
            {
                for (int c = 0; c < 3; c++)
                {
                    double v = buf[i + c] / 255.0;
                    v = (v - 0.5) * gain + 0.5;
                    buf[i + c] = (byte)Math.Clamp(v * 255.0, 0, 255);
                }
            }
            Marshal.Copy(buf, 0, data.Scan0, bytes);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>Converts a GDI Bitmap to a BGRA8 SoftwareBitmap for the OCR engine.</summary>
    private static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = data.Stride * data.Height;
            var buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);

            var software = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);
            software.CopyFromBuffer(buffer.AsBuffer());
            return software;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
