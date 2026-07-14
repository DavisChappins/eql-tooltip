using System.Drawing;
using System.IO;
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

    /// <summary>Optional live-debug sink: (captureBitmap, rawOcrText, pickedName, passLabel).</summary>
    public Action<Bitmap, string, string?, string>? DebugSink { get; set; }

    public TooltipReader(Settings settings, OcrService ocr)
    {
        _settings = settings;
        _ocr = ocr;
    }

    /// <summary>The tight inventory-tooltip box (below-right of the cursor).</summary>
    public Rectangle TooltipRegion(System.Drawing.Point cursor) => new(
        cursor.X + _settings.TooltipOffsetX,
        cursor.Y + _settings.TooltipOffsetY,
        _settings.TooltipWidth,
        _settings.TooltipHeight);

    /// <summary>The wide box around the cursor (Description-window fallback).</summary>
    public Rectangle WideRegion(System.Drawing.Point cursor) => new(
        cursor.X - _settings.CaptureLeft,
        cursor.Y - _settings.CaptureUp,
        _settings.CaptureLeft + _settings.CaptureRight,
        _settings.CaptureUp + _settings.CaptureDown);

    public async Task<TooltipRead> ReadAtAsync(System.Drawing.Point cursor)
    {
        // Pass 1: tight tooltip box below-right of the cursor (reliable inventory-hover case).
        // The cursor is just outside the top-left corner, so the name is near fraction (0, 0).
        // requireConfident: reject bare slot words ("Primary") so on-icon hovers fall to pass 2.
        var tightBmp = ScreenCapture.Capture(TooltipRegion(cursor));
        var tightLines = await _ocr.ReadLinesAsync(tightBmp);
        var tightRaw = string.Join("\n", tightLines.Select(l => l.Text));

        // If pass 1's capture already looks like an item Description window, DON'T accept pass 1's
        // (usually garbage) pick — go straight to pass 2. Inventory tooltips never contain the
        // Exaltation/Unmodified labels, so this can't affect the inventory-hover path.
        bool isDescription = LooksLikeDescriptionWindow(tightLines);
        if (!isDescription)
        {
            var tightName = PickItemName(tightLines, 0.02, 0.02, requireConfident: true);
            DebugSink?.Invoke(tightBmp, tightRaw, tightName, "pass 1 (tight tooltip box)");
            if (!string.IsNullOrWhiteSpace(tightName))
            {
                MaybeDump(tightBmp, tightRaw, tightName);
                return new TooltipRead(tightName, tightRaw, tightBmp);
            }
        }
        else
        {
            DebugSink?.Invoke(tightBmp, tightRaw, null, "pass 1 skipped (description window detected)");
        }
        tightBmp.Dispose();

        // Pass 2: capture a LARGE area around the cursor and OCR it once. If it looks like an item
        // Description window (contains "Description"/"Lore"/"Unmodified"/"Exaltation"), the item
        // name is the green TITLE — the confident line right after the "Description" tab. Otherwise
        // fall back to the longest name-shaped line.
        var region = WideRegion(cursor);
        var bmp = ScreenCapture.Capture(region);
        var lines = await _ocr.ReadLinesAsync(bmp);
        var raw = string.Join("\n", lines.Select(l => l.Text));

        string? name;
        string pass;
        if (LooksLikeDescriptionWindow(lines))
        {
            name = PickDescriptionTitle(lines);
            pass = "pass 2 (description window detected)";
        }
        else
        {
            name = PickItemName(lines, 0, 0, requireConfident: true, preferName: true);
            pass = "pass 2 (wide box, no description window)";
        }

        DebugSink?.Invoke(bmp, raw, name, pass);
        MaybeDump(bmp, raw, name);
        return new TooltipRead(name, raw, bmp);
    }

    private async Task<TooltipRead> ReadRegionAsync(
        Rectangle region, double cursorFracX, double cursorFracY,
        bool requireConfident = false, bool preferName = false, bool topBandOnly = false)
    {
        var bmp = ScreenCapture.Capture(region);
        var lines = await _ocr.ReadLinesAsync(bmp);
        var raw = string.Join("\n", lines.Select(l => l.Text));
        var name = PickItemName(lines, cursorFracX, cursorFracY, requireConfident, preferName, topBandOnly);
        return new TooltipRead(name, raw, bmp);
    }

    /// <summary>
    /// Chooses the item-name line from OCR output. Works for a small inventory tooltip AND a busy
    /// item Description window. The dominant signal is PROXIMITY to the cursor — the name is near
    /// where you point — with smaller bonuses for name-shaped text and repeated lines.
    /// </summary>
    internal static string? PickItemName(
        IReadOnlyList<OcrLine> lines, double cursorFracX = 0.15, double cursorFracY = 0.4,
        bool requireConfident = false, bool preferName = false, bool topBandOnly = false)
    {
        if (lines.Count == 0)
            return null;

        // Normalize OCR box positions to [0,1] using the max extents seen.
        double maxX = Math.Max(1, lines.Max(l => l.Left + 1));
        double maxY = Math.Max(1, lines.Max(l => l.Top + l.Height + 1));

        var cleaned = lines
            .Select(l => new
            {
                Text = Clean(l.Text),
                FracX = l.Left / maxX,
                FracY = (l.Top + l.Height / 2) / maxY
            })
            .Where(x => x.Text.Length > 0)
            .ToList();
        if (cleaned.Count == 0)
            return null;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cleaned)
            counts[c.Text] = counts.GetValueOrDefault(c.Text) + 1;

        string? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (var c in cleaned)
        {
            if (!LooksLikeName(c.Text))
                continue;
            // Pass 1 (tight box) only accepts a CONFIDENT name — not a bare slot word like
            // "Primary"/"Face" — so that hovering directly on an icon falls through to the wide
            // box, which reads the real title above the icon.
            if (requireConfident && !IsConfidentName(c.Text))
                continue;
            // Pass 1 also requires the name to be in the TOP band (where a real inventory tooltip
            // puts the item name). Rejects stray mid-frame fragments when we're actually hovering
            // inside an open Description window, so we fall through to pass 2. (FracX is unreliable
            // to normalize from line boxes, so only the vertical band is checked.)
            if (topBandOnly && c.FracY > 0.30)
                continue;

            int words = c.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            double score;
            if (preferName)
            {
                // Description window: the item name may be anywhere in the frame (the title bar can
                // be clipped; the name also appears in the "Unmodified" dropdown lower down). The
                // name is the LONGEST, most word-rich confident line — stats and UI chrome are short.
                // So score by length/word-count, plus a bonus for repetition.
                score = c.Text.Length + words * 4;
                if (counts[c.Text] > 1) score += 40;
                // Slight preference for lines higher up as a tiebreaker.
                score += (1 - c.FracY) * 2;
            }
            else
            {
                // Tight tooltip box: proximity to the cursor dominates.
                double dx = c.FracX - cursorFracX;
                double dy = c.FracY - cursorFracY;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                score = -dist * 100;
                if (counts[c.Text] > 1) score += 25;
                if (words is >= 2 and <= 8) score += 8;
            }

            if (char.IsUpper(c.Text[0])) score += 3;

            if (score > bestScore)
            {
                bestScore = score;
                best = c.Text;
            }
        }

        return best;
    }

    /// <summary>
    /// True if the OCR text looks like an item Description window (has its tell-tale labels).
    /// </summary>
    internal static bool LooksLikeDescriptionWindow(IReadOnlyList<OcrLine> lines) =>
        LooksLikeDescriptionWindow(lines.Select(l => l.Text));

    internal static bool LooksLikeDescriptionWindow(IEnumerable<string> texts)
    {
        // The Description window has 4-5 "* Exaltation:" rows plus "Ornamentation:" and an
        // "(Un)modified" dropdown. OCR mangles some, but "xaltation" appears several times — that
        // alone is a reliable tell. Match loosely to survive OCR errors (Omamentation, Jnmodified).
        int exalt = 0, other = 0;
        foreach (var t in texts)
        {
            var s = t.Trim();
            if (Regex.IsMatch(s, @"xaltation", RegexOptions.IgnoreCase)) exalt++;
            if (Regex.IsMatch(s, @"(description|[JU]nmodified|amentation|can be upgraded|inspect or upgrade)",
                    RegexOptions.IgnoreCase)) other++;
        }
        return exalt >= 2 || (exalt >= 1 && other >= 1) || other >= 2;
    }

    /// <summary>
    /// Picks the item title from a Description-window capture. The green title is the confident
    /// name on/after the "Description" tab line, and it repeats in the "Unmodified" dropdown — so
    /// the confident name that appears the EARLIEST (topmost) and/or repeats is the title.
    /// </summary>
    internal static string? PickDescriptionTitle(IReadOnlyList<OcrLine> lines)
    {
        var cleaned = lines
            .OrderBy(l => l.Top)
            .Select(l => Clean(l.Text))
            .Where(t => t.Length > 0)
            .ToList();
        if (cleaned.Count == 0)
            return null;

        bool IsName(string s) => LooksLikeName(s) && IsConfidentName(s);

        // Strongest signal: the item name sits on/after the "(Un)modified" dropdown line, which
        // holds the full name. Prefer a confident name at or just after that line.
        for (int i = 0; i < cleaned.Count; i++)
        {
            if (Regex.IsMatch(cleaned[i], @"^[JU]?n?modified\b", RegexOptions.IgnoreCase))
            {
                // Same line may be "Unmodified <name>"; strip the keyword and check the remainder.
                var inline = Regex.Replace(cleaned[i], @"^[JU]?n?modified\b\s*", "", RegexOptions.IgnoreCase).Trim();
                if (inline.Length > 0 && IsName(inline))
                    return inline;
                if (i + 1 < cleaned.Count && IsName(cleaned[i + 1]))
                    return cleaned[i + 1];
            }
        }

        // Next: a confident name that repeats (title bar + dropdown), topmost first.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cleaned) counts[c] = counts.GetValueOrDefault(c) + 1;
        var repeated = cleaned.FirstOrDefault(c => counts[c] > 1 && IsName(c));
        if (repeated is not null)
            return repeated;

        // Fallback: the LONGEST confident name (item names are long; stats/labels are short).
        return cleaned.Where(IsName).OrderByDescending(s => s.Length).FirstOrDefault();
    }

    /// <summary>Overload for tests that only have text (positions default to top-left-ish).</summary>
    internal static string? PickItemName(IReadOnlyList<string> lines)
    {
        var positioned = lines
            .Select((t, i) => new OcrLine(t, Top: i * 20, Height: 16, Left: 0))
            .ToList();
        return PickItemName(positioned);
    }

    // ---- debug dump (enable by creating an "ocrdebug" file in the settings folder) ----
    private void MaybeDump(Bitmap bmp, string raw, string? picked)
    {
        try
        {
            var flag = Path.Combine(Settings.DefaultDirectory, "ocrdebug");
            if (!File.Exists(flag))
                return;

            var dir = Path.Combine(Settings.DefaultDirectory, "debug");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            bmp.Save(Path.Combine(dir, $"capture_{stamp}.png"), System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText(Path.Combine(dir, $"ocr_{stamp}.txt"),
                $"PICKED: {picked}\n\n--- RAW OCR ---\n{raw}");
        }
        catch { /* debug only */ }
    }

    private static readonly Regex StatRow = new(
        @"^(slot|ac|hp|mana|end|wt|weight|size|class|race|dmg|base\s*dmg|delay|ratio|atk|skill|str|sta|agi|dex|wis|int|cha|intelligence|charisma|stamina|agility|strength|dexterity|wisdom|magic|fire|cold|disease|poison|sv|effect|focus|proc|click|worn|cast\s*time|cooldown|required|recommended)\b.*[:.]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // UI chrome / non-name lines that show up in the Description window.
    private static readonly Regex UiNoise = new(
        @"^(description|unmodified|ornamentation|focus exaltation|click exaltation|worn exaltation|proc exaltation|click effect|combat effect|empty|lore|no trade|placeable|attunable|prestige|tier|backpack|this item)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Class-abbreviation list, e.g. "WAR CLR PAL RNG SHD DRU MNK BRD ROG". These read as long,
    // word-rich lines but are not item names.
    private static readonly HashSet<string> ClassAbbrevs = new(StringComparer.OrdinalIgnoreCase)
    {
        "war", "clr", "pal", "rng", "shd", "dru", "mnk", "brd", "rog", "shm", "nec", "wiz",
        "mag", "enc", "ber", "bst",
    };

    // Single words that are EQ slots/keywords, not item names. If a tight-box read yields only one
    // of these, it's not a confident item name (the cursor is on the icon, not the tooltip title).
    private static readonly HashSet<string> SlotWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "primary", "secondary", "range", "ranged", "ammo", "head", "face", "ear", "neck", "shoulder",
        "shoulders", "arms", "back", "wrist", "wrists", "hands", "hand", "fingers", "finger", "chest",
        "legs", "feet", "waist", "mask", "charm", "power", "source", "all", "none", "lore", "magic",
        "attunable", "equipped", "placeable", "augmented", "exaltation", "unmodified", "description",
        // Sizes and other single-word stat values that aren't item names.
        "small", "medium", "large", "tiny", "giant", "empty", "trade", "prestige", "temporary",
        "value", "delay", "ratio", "skill", "size", "weight", "damage",
    };

    /// <summary>
    /// A "confident" item name: at least two words, OR a single word that isn't a slot/keyword and
    /// is reasonably long. Used to gate pass 1 so a bare "Primary" doesn't block the wide-box read.
    /// </summary>
    internal static bool IsConfidentName(string s)
    {
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
            return !words.All(w => SlotWords.Contains(w));
        return s.Length >= 5 && !SlotWords.Contains(s);
    }

    private static bool LooksLikeName(string s)
    {
        if (s.Length < 3) return false;
        if (s.Contains(':')) return false;                 // stat rows use colons
        if (StatRow.IsMatch(s)) return false;
        if (UiNoise.IsMatch(s)) return false;

        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Reject a class-abbreviation list (most tokens are known class codes).
        if (words.Length >= 2 && words.Count(w => ClassAbbrevs.Contains(w)) >= words.Length - 1)
            return false;

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
        t = Regex.Replace(t, @"\s*\((?:augmented|exaltation)\)\s*$", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s*\+\d+\s*$", "");         // strip "+5" upgrade marker
        t = t.Trim();
        return t;
    }
}
