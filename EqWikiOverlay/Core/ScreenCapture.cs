using System.Drawing;
using System.Runtime.InteropServices;

namespace EqWikiOverlay.Core;

/// <summary>
/// Captures a rectangular region of the screen via GDI BitBlt. Simple and reliable for grabbing
/// a static area (EQ's item tooltip) on demand — no continuous capture session needed.
/// Coordinates are physical screen pixels.
/// </summary>
public static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hDest, int xDest, int yDest, int w, int h,
        IntPtr hSrc, int xSrc, int ySrc, int rop);

    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    /// <summary>
    /// Captures the given screen rectangle. Returns a new Bitmap the caller must dispose.
    /// The rect is clamped to the virtual screen bounds.
    /// </summary>
    public static Bitmap Capture(Rectangle region)
    {
        region = ClampToVirtualScreen(region);
        if (region.Width <= 0 || region.Height <= 0)
            region = new Rectangle(region.X, region.Y, 1, 1);

        IntPtr desktop = GetDesktopWindow();
        IntPtr srcDc = GetWindowDC(desktop);
        IntPtr memDc = CreateCompatibleDC(srcDc);
        IntPtr bmp = CreateCompatibleBitmap(srcDc, region.Width, region.Height);
        IntPtr oldBmp = SelectObject(memDc, bmp);

        try
        {
            BitBlt(memDc, 0, 0, region.Width, region.Height, srcDc, region.X, region.Y, SRCCOPY | CAPTUREBLT);
            // Copy into a managed Bitmap so we can dispose GDI resources immediately.
            using var gdiBmp = Image.FromHbitmap(bmp);
            return new Bitmap(gdiBmp);
        }
        finally
        {
            SelectObject(memDc, oldBmp);
            DeleteObject(bmp);
            DeleteDC(memDc);
            ReleaseDC(desktop, srcDc);
        }
    }

    private static Rectangle ClampToVirtualScreen(Rectangle r)
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        int x = Math.Max(vs.Left, r.Left);
        int y = Math.Max(vs.Top, r.Top);
        int right = Math.Min(vs.Right, r.Right);
        int bottom = Math.Min(vs.Bottom, r.Bottom);
        return Rectangle.FromLTRB(x, y, right, bottom);
    }
}
