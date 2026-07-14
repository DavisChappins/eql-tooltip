using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace EqWikiOverlay.Ui;

/// <summary>Live view of the last OCR capture: image, raw text, and what was picked.</summary>
public partial class DebugWindow : Window
{
    public DebugWindow()
    {
        InitializeComponent();
    }

    /// <summary>Push a new reading to the window (call on the UI thread).</summary>
    public void Update(Bitmap capture, string rawText, string? picked, string pass)
    {
        PickedText.Text = string.IsNullOrEmpty(picked) ? "(none)" : picked;
        PassText.Text = pass;
        RawText.Text = rawText;
        CaptureImage.Source = ToImageSource(capture);
    }

    public void SetResolved(string resolved)
    {
        ResolvedText.Text = resolved;
    }

    private static BitmapImage ToImageSource(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
