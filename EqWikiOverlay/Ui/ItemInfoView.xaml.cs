using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using EqWikiOverlay.Models;

namespace EqWikiOverlay.Ui;

public partial class ItemInfoView : System.Windows.Controls.UserControl
{
    /// <summary>Raised when the user clicks the close (✕) button.</summary>
    public event Action? CloseRequested;

    public ItemInfoView()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void WikiButton_Click(object sender, RoutedEventArgs e)
    {
        var url = (DataContext as ItemPanelViewModel)?.PageUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No browser / bad URL — ignore.
        }
    }
}
