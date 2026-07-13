using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using EqWikiOverlay.Models;

namespace EqWikiOverlay.Ui;

/// <summary>
/// An always-visible panel docked to the right edge of a chosen screen (ideally a second monitor).
/// Updates in place as items are looked up. No overlay-over-game complexity.
/// </summary>
public partial class SidePanelWindow : Window, IInfoWindow
{
    public SidePanelWindow(ItemPanelViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Info.DataContext = vm;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => DockToPreferredScreen();
    }

    /// <summary>Docks to the right edge of the last screen (a second monitor if present).</summary>
    private void DockToPreferredScreen()
    {
        var screens = Screen.AllScreens;
        var target = screens.Length > 1
            ? screens.First(s => !s.Primary)
            : screens[0];

        var wa = target.WorkingArea; // device pixels
        var src = PresentationSource.FromVisual(this);
        double dx = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double dy = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

        Height = wa.Height * dy;
        Top = wa.Top * dy;
        Left = (wa.Right * dx) - Width;
    }

    // The side panel is persistent; ShowNear just makes sure it's visible.
    public void ShowNear(Point screenPoint)
    {
        if (!IsVisible) Show();
    }

    public void HidePanel() => Hide();
    public void ClosePanel() => Close();
}
