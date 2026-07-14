using EqWikiOverlay.Models;
using EqWikiOverlay.Ui;
using EqWikiOverlay.Wiki;

namespace EqWikiOverlay.Core;

/// <summary>
/// Drives a lookup when the hotkey is pressed:
///   1. read the cursor position;
///   2. OCR the EQ tooltip region below-right of the cursor;
///   3. pick the item name and resolve it against the wiki (via cache);
///   4. show the result in the overlay, pinned top-right to where the cursor was.
/// </summary>
public sealed class LookupCoordinator
{
    private readonly Settings _settings;
    private readonly TooltipReader _reader;
    private readonly WikiCache _cache;
    private readonly ItemPanelViewModel _vm;
    private readonly Func<IInfoWindow> _windowFactory;
    private readonly Action<Action> _onUi;

    private IInfoWindow? _window;
    private int _busy; // 0/1 guard so overlapping hotkey presses don't stack

    public bool OcrAvailable { get; }

    /// <summary>Optional: reports the resolved wiki outcome (for the debug window).</summary>
    public Action<string>? ResolvedSink { get; set; }

    public LookupCoordinator(
        Settings settings,
        TooltipReader reader,
        bool ocrAvailable,
        WikiCache cache,
        ItemPanelViewModel vm,
        Func<IInfoWindow> windowFactory,
        Action<Action> marshalToUi)
    {
        _settings = settings;
        _reader = reader;
        OcrAvailable = ocrAvailable;
        _cache = cache;
        _vm = vm;
        _windowFactory = windowFactory;
        _onUi = marshalToUi;
    }

    /// <summary>Fired by the global hotkey.</summary>
    public async void OnHotkey()
    {
        if (System.Threading.Interlocked.Exchange(ref _busy, 1) == 1)
            return;

        try
        {
            var cursor = System.Windows.Forms.Cursor.Position; // physical px
            var anchor = new Point(cursor.X, cursor.Y);

            if (!OcrAvailable)
            {
                _onUi(() =>
                {
                    _vm.ShowMessage("OCR unavailable",
                        "Windows OCR isn't available. Install the English OCR language feature and restart.");
                    ShowAt(anchor);
                });
                return;
            }

            // Capture & OCR FIRST, before showing our panel — otherwise the panel (which is
            // capturable and sits just left of the cursor) could be read by our own OCR.
            using var read = await _reader.ReadAtAsync(cursor);

            if (string.IsNullOrWhiteSpace(read.ItemName))
            {
                _onUi(() =>
                {
                    _vm.ShowMessage("No text found",
                        "Couldn't read an item name under the cursor. Hover the item (or open its " +
                        "Description window) so the name is visible, then press the hotkey.");
                    ShowAt(anchor);
                });
                return;
            }

            _onUi(() =>
            {
                _vm.ShowLoading(read.ItemName);
                ShowAt(anchor);
            });

            var result = await _cache.GetAsync(read.ItemName);

            ResolvedSink?.Invoke(result.Found
                ? $"OCR \"{read.ItemName}\"  →  wiki \"{result.PageTitle}\"  ✓"
                : $"OCR \"{read.ItemName}\"  →  no wiki page found");

            _onUi(() =>
            {
                if (result.Found)
                {
                    _vm.ShowResult(result);
                }
                else
                {
                    _vm.ShowMessage(read.ItemName,
                        $"No wiki page found.\nOCR read: \"{read.ItemName}\"\n\n" +
                        "If the name is misread, widen the capture region or hover more precisely.");
                }
            });
        }
        catch (Exception ex)
        {
            _onUi(() => _vm.ShowMessage("Lookup error", ex.Message));
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>Look up a known item name directly (tray "Test lookup"), bypassing OCR.</summary>
    public async void LookupName(string itemName)
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var anchor = new Point(cursor.X, cursor.Y);
        _onUi(() =>
        {
            _vm.ShowLoading(itemName);
            ShowAt(anchor);
        });

        var result = await _cache.GetAsync(itemName);
        _onUi(() =>
        {
            if (result.Found) _vm.ShowResult(result);
            else _vm.ShowMessage(itemName, "No wiki page found for this item.");
        });
    }

    /// <summary>Hide the panel (hotkey released).</summary>
    public void Hide() => _onUi(() => _window?.HidePanel());

    private void ShowAt(Point anchor) => EnsureWindow().ShowNear(anchor);

    private IInfoWindow EnsureWindow()
    {
        _window ??= _windowFactory();
        return _window;
    }

}
