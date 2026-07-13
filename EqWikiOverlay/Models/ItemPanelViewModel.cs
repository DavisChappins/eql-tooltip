using System.Collections.ObjectModel;
using System.ComponentModel;

namespace EqWikiOverlay.Models;

/// <summary>Bindable state for the item info panel, shared by all display modes.</summary>
public sealed class ItemPanelViewModel : INotifyPropertyChanged
{
    private string _title = "";
    private string _message = "";
    private string? _pageUrl;
    private string _status = "";
    private bool _isBusy;

    public string Title
    {
        get => _title;
        set { _title = value; Raise(nameof(Title)); }
    }

    /// <summary>A single gray message line (loading / error / no-result). Empty when showing sections.</summary>
    public string Message
    {
        get => _message;
        set { _message = value; Raise(nameof(Message)); Raise(nameof(HasMessage)); }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>Structured, styled result sections.</summary>
    public ObservableCollection<WikiSection> Sections { get; } = new();

    public bool HasSections => Sections.Count > 0;

    public string? PageUrl
    {
        get => _pageUrl;
        set { _pageUrl = value; Raise(nameof(PageUrl)); Raise(nameof(HasPageUrl)); }
    }

    public bool HasPageUrl => !string.IsNullOrEmpty(_pageUrl);

    public string Status
    {
        get => _status;
        set { _status = value; Raise(nameof(Status)); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; Raise(nameof(IsBusy)); }
    }

    public void ShowLoading(string itemName)
    {
        Title = itemName;
        ClearSections();
        PageUrl = null;
        Status = "";
        Message = "Looking up…";
        IsBusy = true;
    }

    public void ShowResult(WikiItem item)
    {
        IsBusy = false;
        PageUrl = item.PageUrl;
        Status = "Sourced from eqlwiki.com";

        if (item.Found && item.Sections.Count > 0)
        {
            Title = item.PageTitle ?? item.CanonicalName;
            Message = "";
            ClearSections();
            foreach (var s in item.Sections)
                Sections.Add(s);
            Raise(nameof(HasSections));
        }
        else
        {
            Title = item.PageTitle ?? item.CanonicalName;
            ClearSections();
            Message = "No wiki details found for this item.";
        }
    }

    public void ShowMessage(string title, string body)
    {
        IsBusy = false;
        Title = title;
        ClearSections();
        PageUrl = null;
        Status = "";
        Message = body;
    }

    private void ClearSections()
    {
        Sections.Clear();
        Raise(nameof(HasSections));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
