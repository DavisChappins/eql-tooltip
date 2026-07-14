using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EqWikiOverlay.Core;

/// <summary>
/// User-configurable settings, persisted as JSON under %AppData%\EqWikiOverlay\settings.json.
/// </summary>
public sealed class Settings
{
    /// <summary>Hold-to-show hotkey. Panel shows while held, hides on release. e.g. "Shift+A".</summary>
    public string Hotkey { get; set; } = "Shift+A";

    // ---- OCR capture regions (extents around the cursor, in screen pixels) ----
    // Lookups try a TIGHT box first (the small inventory tooltip below-right of the pointer). Only
    // if that finds no name do they fall back to the WIDE box (for an item Description window, whose
    // name is in the title bar above/beside the cursor). Two passes keeps plain inventory hovers
    // clean (no stat-panel noise) while still handling opened item windows.

    // Tight tooltip box (below-right of the cursor) — the reliable inventory-hover case.
    public int TooltipOffsetX { get; set; } = 4;   // px right of cursor for the box's left edge
    public int TooltipOffsetY { get; set; } = 4;   // px below cursor for the box's top edge
    public int TooltipWidth { get; set; } = 460;
    public int TooltipHeight { get; set; } = 340;

    // Wide box for the Description window fallback. The item name is the window TITLE, which can be
    // well above the cursor when hovering the icon or stats, so the box reaches FAR up and to both
    // sides to guarantee the title bar is captured wherever in the window you hover.
    public int CaptureLeft { get; set; } = 260;
    public int CaptureRight { get; set; } = 560;
    public int CaptureUp { get; set; } = 620;
    public int CaptureDown { get; set; } = 160;

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
