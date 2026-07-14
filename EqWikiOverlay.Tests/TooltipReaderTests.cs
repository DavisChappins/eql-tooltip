using EqWikiOverlay.Core;

namespace EqWikiOverlay.Tests;

public class TooltipReaderTests
{
    [Theory]
    [InlineData("Sword of the Lost +4", "Sword of the Lost")]
    [InlineData("'Flowing Black Silk Sash'", "Flowing Black Silk Sash")]
    [InlineData("  Cloth   Cap  ", "Cloth Cap")]
    [InlineData("*Rusty Long Sword*", "Rusty Long Sword")]
    [InlineData("Rod of the Protecting Winds +5 (Augmented)", "Rod of the Protecting Winds")]
    public void CleanNormalizesOcrNoise(string raw, string expected)
    {
        Assert.Equal(expected, TooltipReader.Clean(raw));
    }

    [Fact]
    public void PickItemName_InventoryTooltip_ChoosesNameOverStats()
    {
        var lines = new[]
        {
            "Cloth Cap",
            "Slot: HEAD",
            "AC: 2",
            "WT: 0.2 Size: SMALL",
            "Class: ALL",
        };
        Assert.Equal("Cloth Cap", TooltipReader.PickItemName(lines));
    }

    [Fact]
    public void PickItemName_DescriptionWindow_ChoosesRepeatedTitle()
    {
        // OCR of the item Description window (from the screenshot). The name appears in the title
        // bar, next to the icon, and in the "Unmodified" dropdown — three times.
        var lines = new[]
        {
            "Rod of the Protecting Winds +5 (Augmented)",
            "Description",
            "Rod of the Protecting Winds +5",
            "Lore Equipped, No Trade, Placeable",
            "Class: ENC",
            "Race: ALL",
            "Primary",
            "Size: LARGE   AC: 15   Base Dmg: 53",
            "Weight: 2.7   Mana: 113   Delay: 45",
            "Skill: 2H Blunt",
            "Unmodified   Rod of the Protecting Winds +5",
            "Ornamentation: empty",
            "Click Exaltation: Rod of the Protecting Winds (Exaltation)",
            "Click Effect: Rune III (Must Equip)",
        };

        Assert.Equal("Rod of the Protecting Winds", TooltipReader.PickItemName(lines));
    }

    [Fact]
    public void PickItemName_SkipsUiChrome()
    {
        var lines = new[]
        {
            "Description",
            "Unmodified",
            "Ornamentation: empty",
            "Sword of the Lost",
            "Slot: PRIMARY",
        };
        Assert.Equal("Sword of the Lost", TooltipReader.PickItemName(lines));
    }

    [Fact]
    public void PickItemName_ReturnsNullForEmpty()
    {
        Assert.Null(TooltipReader.PickItemName(Array.Empty<string>()));
    }

    // Real OCR of a Sword of the Lost Description window (from the debug dumps).
    private static readonly string[] SwordDescOcr =
    {
        "Class: PAL SHD", "Race: Al I", "Primary", "Tier4 0/16", "lerge", "Place", "Size.", "tem",
        "Item", "This item can be upgraded.", "LARGE", "Base Dmg.", "21", "6.1", "Delay:", "40",
        "II: IH Slashing", "8", "Modified", "Sword of the Lost +4", "Omamentation: empty",
        "Focus Exaltation: empty", "Click Exaltation: empty", "Wom Exaltation: empty",
    };

    private static readonly string[] InventoryOcr =
    {
        "Range;", "Ammo", "Bark Splinted Shoulderpads +4", "No Trade", "Class: WAR CLR PAL", "Shoulders",
    };

    private static System.Collections.Generic.List<EqWikiOverlay.Core.OcrLine> Positioned(string[] raw) =>
        raw.Select((t, i) => new EqWikiOverlay.Core.OcrLine(t, Top: i * 20, Height: 16, Left: 0)).ToList();

    [Fact]
    public void DescriptionWindow_IsDetected()
    {
        Assert.True(TooltipReader.LooksLikeDescriptionWindow(SwordDescOcr));
        // A plain inventory tooltip is NOT a description window.
        Assert.False(TooltipReader.LooksLikeDescriptionWindow(InventoryOcr));
    }

    [Fact]
    public void DescriptionTitle_PicksNameFromModifiedLine()
    {
        var title = TooltipReader.PickDescriptionTitle(Positioned(SwordDescOcr));
        Assert.Equal("Sword of the Lost", title);
    }

    [Fact]
    public void DescriptionTitle_MorningStar_KeepsGarbledNameForFuzzyLookup()
    {
        var raw = new[]
        {
            "Class: WAR CLR PAL RNG SHD DRU MNK BRD ROG", "SHM", "Race: Al I", "Primary Secondary",
            "MEDIUM", "Base Dmg:", "9", "IH Blunt", "Jnmodified",
            "Enchanted Fine Steel Mominq Star +4", "Omamentation: empty", "Focus Exaltation: empty",
            "Click Exaltation: empty", "Proc Exaltation: empty",
        };
        var title = TooltipReader.PickDescriptionTitle(Positioned(raw));
        Assert.Equal("Enchanted Fine Steel Mominq Star", title); // OCR-corrected later by the wiki search
    }

    [Theory]
    [InlineData("Primary", false)]
    [InlineData("Face", false)]
    [InlineData("All", false)]
    [InlineData("Fang", false)]                       // short single word -> not confident
    [InlineData("SMALL", false)]                      // size value, not a name
    [InlineData("Primary Secondary", false)]          // two slot words
    [InlineData("Weight", false)]
    [InlineData("Leering Mask", true)]
    [InlineData("Sword of the Lost", true)]
    [InlineData("Stiletto", true)]                    // long single word -> confident
    public void IsConfidentName_GatesSlotWords(string s, bool expected)
    {
        Assert.Equal(expected, TooltipReader.IsConfidentName(s));
    }

    [Fact]
    public void PickItemName_ConfidentGate_RejectsBareSlotWord()
    {
        // Tight box on the icon reads only "Primary" — pass 1 must return null so we fall to pass 2.
        var lines = new List<EqWikiOverlay.Core.OcrLine>
        {
            new("Primary", Top: 0, Height: 16, Left: 0),
            new("AC: 7",   Top: 20, Height: 16, Left: 0),
        };
        Assert.Null(TooltipReader.PickItemName(lines, 0.02, 0.02, requireConfident: true));
        // Without the gate (pass 2), it would still pick "Primary" as the only name-ish line —
        // but pass 2 uses the wide box that actually contains the real title.
    }

    [Fact]
    public void PickItemName_TopBandOnly_RejectsMidFrameFragment()
    {
        // Hovering inside an open Description window with the tight box: a stray fragment
        // ("ge Place") sits mid-frame. topBandOnly must reject it so pass 1 fails -> pass 2 runs.
        var lines = new List<EqWikiOverlay.Core.OcrLine>
        {
            new("Class WAR CLR PAL SHD BRD", Top: 0,   Height: 16, Left: 0),
            new("Back",                       Top: 30,  Height: 16, Left: 0),
            new("ge Place",                   Top: 200, Height: 16, Left: 40),  // mid-frame junk
            new("Splinted Bronze Cloak",      Top: 320, Height: 16, Left: 200),
        };
        Assert.Null(TooltipReader.PickItemName(lines, 0.02, 0.02, requireConfident: true, topBandOnly: true));
    }

    [Fact]
    public void PickItemName_TopBandOnly_AcceptsTopLeftName()
    {
        // Real inventory tooltip: name is the top-left line -> accepted.
        var lines = new List<EqWikiOverlay.Core.OcrLine>
        {
            new("Bark Splinted Shoulderpads", Top: 4,  Height: 16, Left: 4),
            new("No Trade",                    Top: 26, Height: 16, Left: 4),
            new("Class: WAR CLR PAL",          Top: 48, Height: 16, Left: 4),
        };
        Assert.Equal("Bark Splinted Shoulderpads",
            TooltipReader.PickItemName(lines, 0.02, 0.02, requireConfident: true, topBandOnly: true));
    }

    [Fact]
    public void PickItemName_PrefersNameNearCursorOverDistantFragment()
    {
        // A stray 2-word fragment ("Z Max") far from the cursor should lose to the real name that
        // sits right at the cursor position.
        var lines = new List<EqWikiOverlay.Core.OcrLine>
        {
            new("Z Max",        Top: 10,  Height: 16, Left: 0),    // top-left, far from cursor
            new("Leering Mask", Top: 400, Height: 16, Left: 300),  // near cursor
            new("Class: ALL",   Top: 420, Height: 16, Left: 300),
        };
        // Cursor near the lower-right of the capture.
        var picked = TooltipReader.PickItemName(lines, cursorFracX: 0.9, cursorFracY: 0.9);
        Assert.Equal("Leering Mask", picked);
    }

    [Fact]
    public void PickItemName_PreferTop_PicksRepeatedTitleWhenCursorOnIcon()
    {
        // Description window (cursor on the icon): title at top + green name repeat; stats are
        // lower. preferName must pick the repeated title, not a stat word.
        var lines = new List<EqWikiOverlay.Core.OcrLine>
        {
            new("Serrated Bone Dirk",   Top: 10,  Height: 16, Left: 200),  // title bar
            new("Description",          Top: 40,  Height: 16, Left: 220),
            new("Serrated Bone Dirk",   Top: 80,  Height: 16, Left: 120),  // green name by icon
            new("Attunable, Placeable", Top: 110, Height: 16, Left: 120),
            new("Primary Secondary",    Top: 170, Height: 16, Left: 120),
            new("Skill: 1H Piercing",   Top: 260, Height: 16, Left: 120),
        };
        var picked = TooltipReader.PickItemName(lines, 0, 0, requireConfident: true, preferName: true);
        Assert.Equal("Serrated Bone Dirk", picked);
    }
}
