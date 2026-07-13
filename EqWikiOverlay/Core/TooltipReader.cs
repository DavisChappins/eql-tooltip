using System.Drawing;
using System.Text.RegularExpressions;

namespace EqWikiOverlay.Core;

/// <summary>Result of reading the tooltip under the cursor. Owns the capture bitmap.</summary>
public sealed record TooltipRead(string? ItemName, string RawText, Bitmap Capture) : IDisposable
{
    public void Dispose() => Capture.Dispose();
}

/// <summary>
/// Captures the screen region where EQ draws its item tooltip (below-right of the cursor),
/// OCRs it, and picks the most likely item-name line.
/// </summary>
public sealed class TooltipReader
{
    private readonly Settings _settings;
    private readonly OcrService _ocr;

    public TooltipReader(Settings settings, OcrService ocr)
    {
        _settings = settings;
        _ocr = ocr;
    }

    /// <summary>Screen rectangle to capture given the cursor position.</summary>
    public Rectangle RegionForCursor(System.Drawing.Point cursor) => new(
        cursor.X + _settings.CaptureOffsetX,
        cursor.Y + _settings.CaptureOffsetY,
        _settings.CaptureWidth,
        _settings.CaptureHeight);

    public async Task<TooltipRead> ReadAtAsync(System.Drawing.Point cursor)
    {
        var region = RegionForCursor(cursor);
        var bmp = ScreenCapture.Capture(region);

        var lines = await _ocr.ReadLinesAsync(bmp);
        var raw = string.Join("\n", lines.Select(l => l.Text));
        var name = PickItemName(lines.Select(l => l.Text).ToList());

        return new TooltipRead(name, raw, bmp);
    }

    /// <summary>
    /// Chooses the item-name line from OCR output. EQ tooltips lead with the item name; the lines
    /// that follow are stat rows ("Slot: HEAD", "AC: 2", "WT: 0.2"). So the first line that looks
    /// like a name (letters/spaces, not a "Label: value" stat row) wins.
    /// </summary>
    internal static string? PickItemName(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            var cleaned = Clean(line);
            if (LooksLikeName(cleaned))
                return cleaned;
        }
        // Fallback: first non-empty cleaned line.
        foreach (var line in lines)
        {
            var cleaned = Clean(line);
            if (cleaned.Length > 0)
                return cleaned;
        }
        return null;
    }

    private static readonly Regex StatRow = new(
        @"^(slot|ac|hp|mana|wt|weight|size|class|race|dmg|delay|atk|str|sta|agi|dex|wis|int|cha|magic|fire|cold|disease|poison|effect|focus|recommended|required)\b.*[:.]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool LooksLikeName(string s)
    {
        if (s.Length < 3) return false;
        if (StatRow.IsMatch(s)) return false;
        if (s.Contains(':')) return false;                 // stat rows use colons
        int letters = s.Count(char.IsLetter);
        return letters >= 3 && letters >= s.Length / 2;    // mostly letters/spaces
    }

    /// <summary>Trims OCR noise and the EQ Legends "+N" suffix.</summary>
    internal static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Trim();
        t = Regex.Replace(t, @"\s+", " ");
        t = t.Trim('\'', '"', '`', '.', ',', '*', '|', '-', ' ');
        t = Regex.Replace(t, @"\s*\+\d+\s*$", "");         // strip "+4" upgrade marker
        return t.Trim();
    }
}
