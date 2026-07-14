using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using EqWikiOverlay.Core;
using EqWikiOverlay.Models;
using EqWikiOverlay.Ui;
using EqWikiOverlay.Wiki;
using WpfApp = System.Windows.Application;
using WinForms = System.Windows.Forms;

namespace EqWikiOverlay;

public partial class App : WpfApp
{
    private Mutex? _singleInstance;
    private Settings _settings = null!;
    private HttpClient _http = null!;
    private WikiCache _cache = null!;
    private OcrService _ocr = null!;
    private TooltipReader _reader = null!;
    private HoldHotkey? _hotkey;
    private LookupCoordinator? _coordinator;
    private ItemPanelViewModel _vm = null!;
    private TrayIcon? _tray;
    private Ui.DebugWindow? _debugWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, "EqWikiOverlay.SingleInstance", out bool isNew);
        if (!isNew)
        {
            WinForms.MessageBox.Show("EQ Wiki Overlay is already running (check the system tray).",
                "EQ Wiki Overlay");
            Shutdown();
            return;
        }

        // WPF tray apps must not exit when the (absent) main window closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = Settings.Load();
        _vm = new ItemPanelViewModel();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        var provider = new EqlWikiProvider(_http, _settings.WikiApiUrl, _settings.WikiSource);
        var dbPath = Path.Combine(Settings.DefaultDirectory, "wikicache.db");
        _cache = new WikiCache(provider, dbPath);

        _ocr = new OcrService();
        _reader = new TooltipReader(_settings, _ocr);
        _reader.DebugSink = (bmp, raw, picked, pass) =>
        {
            // Clone the bitmap for the UI thread (the reader disposes the original).
            var copy = new System.Drawing.Bitmap(bmp);
            RunOnUi(() => _debugWindow?.Update(copy, raw, picked, pass));
        };

        _coordinator = new LookupCoordinator(
            _settings, _reader, _ocr.Available, _cache, _vm,
            windowFactory: CreateWindow,
            marshalToUi: RunOnUi);
        _coordinator.ResolvedSink = info => RunOnUi(() => _debugWindow?.SetResolved(info));

        StartHotkey();

        if (!_ocr.Available)
        {
            WinForms.MessageBox.Show(
                "Windows OCR isn't available on this system. Install the English OCR feature " +
                "(Settings > Apps > Optional features > Add a feature > search 'OCR') and restart.",
                "EQ Wiki Overlay");
        }

        _tray = new TrayIcon(_settings, this);
        _tray.Show();
    }

    private IInfoWindow CreateWindow() => new OverlayWindow(_vm);

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    /// <summary>Fires a wiki lookup with a typed item name (tray "Test lookup..."). No OCR/EQ.</summary>
    public void TestLookup(string itemName) => _coordinator?.LookupName(itemName);

    /// <summary>Triggers the OCR-at-cursor path (same as the hotkey), for the tray menu.</summary>
    public void TriggerHotkeyLookup() => _coordinator?.OnHotkey();

    /// <summary>Clears the wiki cache (tray menu) so lookups re-fetch fresh.</summary>
    public void ClearCache() => _cache?.Clear();

    /// <summary>Opens (or focuses) the live OCR debug window.</summary>
    public void ShowDebugWindow()
    {
        RunOnUi(() =>
        {
            if (_debugWindow is null)
            {
                _debugWindow = new Ui.DebugWindow();
                _debugWindow.Closed += (_, _) => _debugWindow = null;
            }
            _debugWindow.Show();
            _debugWindow.Activate();
        });
    }

    // Hold-to-show: panel appears while the hotkey is held, hides on release.
    private void StartHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = new HoldHotkey(_settings.Hotkey, Dispatcher);
        _hotkey.Held += () => _coordinator!.OnHotkey();
        _hotkey.Released += () => _coordinator!.Hide();
        _hotkey.Start();
    }

    /// <summary>Changes the hold-to-show hotkey at runtime (tray "Set hotkey…").</summary>
    public void ChangeHotkey(string hotkey)
    {
        _settings.Hotkey = hotkey;
        _settings.Save();
        StartHotkey();
    }

    public string CurrentHotkey => _settings.Hotkey;

    public void ExitApp() => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkey?.Dispose();
        _cache?.Dispose();
        _http?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
