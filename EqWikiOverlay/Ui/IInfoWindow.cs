using System.Windows;

namespace EqWikiOverlay.Ui;

/// <summary>Common surface for the three display modes so the orchestrator can treat them alike.</summary>
public interface IInfoWindow
{
    /// <summary>Show the panel, positioned relative to the given screen point (cursor).</summary>
    void ShowNear(Point screenPoint);

    void HidePanel();
    void ClosePanel();
}
