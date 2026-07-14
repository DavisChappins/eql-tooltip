using System.Windows;
using System.Windows.Interop;
using EqWikiOverlay.Core;
using EqWikiOverlay.Models;

namespace EqWikiOverlay.Ui;

/// <summary>
/// Transparent, click-through, always-on-top overlay drawn over EQ (windowed/borderless).
/// Its TOP-RIGHT corner is pinned to the cursor, so the panel sits to the LEFT of the pointer —
/// clear of EQ's own tooltip, which appears below-and-right of the cursor.
/// Uses WS_EX_TRANSPARENT + WS_EX_NOACTIVATE so it never steals focus or eats clicks, and
/// WDA_EXCLUDEFROMCAPTURE so it won't appear in the OCR capture of the game.
/// </summary>
public partial class OverlayWindow : Window, IInfoWindow
{
    private Point _anchorScreenPx;   // cursor position in physical screen pixels

    public OverlayWindow(ItemPanelViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Info.DataContext = vm;
        Info.CloseRequested += HidePanel;
        SourceInitialized += OnSourceInitialized;
        // Re-anchor whenever the content resizes (e.g. loading -> result changes height).
        SizeChanged += (_, _) => Reposition();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // NOTE: no WS_EX_TRANSPARENT here — the panel must receive clicks so the close (✕) and
        // "Open wiki page" buttons work. WS_EX_NOACTIVATE keeps it from stealing focus/input from
        // EQ, and the panel sits to the LEFT of the cursor so it's not under the crosshair.
        int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        ex |= Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);

        // The panel is capturable (screenshots/recorders see it). It sits to the LEFT of the cursor
        // while our OCR reads the region to the RIGHT, so it never appears in our own captures.
    }

    /// <param name="screenPoint">Cursor position in physical screen pixels.</param>
    public void ShowNear(Point screenPoint)
    {
        _anchorScreenPx = screenPoint;
        if (!IsVisible) Show();
        Reposition();
    }

    private void Reposition()
    {
        // Convert the physical-pixel cursor anchor to WPF DIPs for this window.
        var src = PresentationSource.FromVisual(this);
        double sx = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double sy = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

        double anchorX = _anchorScreenPx.X * sx;
        double anchorY = _anchorScreenPx.Y * sy;

        // Pin top-right corner to the cursor: window extends left and down.
        Left = anchorX - ActualWidth;
        Top = anchorY;

        ClampToWorkArea();
    }

    private void ClampToWorkArea()
    {
        // VirtualScreen* properties are in DIPs and span all monitors.
        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;
        double bottom = top + SystemParameters.VirtualScreenHeight;

        if (Left < left) Left = left;
        if (Top + ActualHeight > bottom) Top = Math.Max(top, bottom - ActualHeight);
        if (Top < top) Top = top;
    }

    public void HidePanel() => Hide();
    public void ClosePanel() => Close();
}
