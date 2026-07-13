using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EqWikiOverlay.Core;

public enum DisplayMode
{
    Popup,
    Overlay,
    SidePanel
}

/// <summary>
/// User-configurable settings, persisted as JSON under %AppData%\EqWikiOverlay\settings.json.
/// </summary>
public sealed class Settings
{
    /// <summary>Hold-to-show hotkey. Panel shows while held, hides on release. e.g. "Shift+A".</summary>
    public string Hotkey { get; set; } = "Shift+A";

    // ---- OCR capture region (relative to the cursor, in screen pixels) ----
    // EQ draws its item tooltip below-and-right of the pointer, so we capture a box whose
    // top-left sits a little below-right of the cursor.
    public int CaptureOffsetX { get; set; } = 4;   // px right of cursor for capture's left edge
    public int CaptureOffsetY { get; set; } = 4;   // px below cursor for capture's top edge
    public int CaptureWidth { get; set; } = 460;
    public int CaptureHeight { get; set; } = 360;

    /// <summary>Which window style shows the wiki info.</summary>
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Overlay;

    /// <summary>MediaWiki API base URL for the primary wiki source.</summary>
    public string WikiApiUrl { get; set; } = "https://eqlwiki.com/api.php";

    /// <summary>Short source key used in the cache and UI, e.g. "eqlwiki".</summary>
    public string WikiSource { get; set; } = "eqlwiki";

    [JsonIgnore]
    public string FilePath { get; private set; } = DefaultPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EqWikiOverlay");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "settings.json");

    public static Settings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.FilePath = path;
                    return loaded;
                }
            }
        }
        catch
        {
            // Corrupt/unreadable settings fall back to defaults rather than crashing startup.
        }

        return new Settings { FilePath = path };
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
