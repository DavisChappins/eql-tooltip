using System.Windows;
using System.Windows.Input;
using EqWikiOverlay.Models;

namespace EqWikiOverlay.Ui;

/// <summary>
/// Simple always-on-top popup near the cursor. Works even over true-fullscreen EQ on a second
/// monitor. Click-drag to move; press Escape or click elsewhere to dismiss.
/// </summary>
public partial class PopupWindow : Window, IInfoWindow
{
    public PopupWindow(ItemPanelViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Info.DataContext = vm;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
    }

    public void ShowNear(Point screenPoint)
    {
        Left = screenPoint.X + 16;
        Top = screenPoint.Y + 16;
        ClampToWorkArea();
        if (!IsVisible) Show();
        ClampToWorkArea();
    }

    private void ClampToWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        if (Left + ActualWidth > wa.Right) Left = Math.Max(wa.Left, wa.Right - ActualWidth);
        if (Top + ActualHeight > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - ActualHeight);
        if (Left < wa.Left) Left = wa.Left;
        if (Top < wa.Top) Top = wa.Top;
    }

    public void HidePanel() => Hide();
    public void ClosePanel() => Close();
}
