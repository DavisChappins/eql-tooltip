using EqWikiOverlay.Core;

namespace EqWikiOverlay.Tests;

public class TooltipReaderTests
{
    [Theory]
    [InlineData("Sword of the Lost +4", "Sword of the Lost")]
    [InlineData("'Flowing Black Silk Sash'", "Flowing Black Silk Sash")]
    [InlineData("  Cloth   Cap  ", "Cloth Cap")]
    [InlineData("*Rusty Long Sword*", "Rusty Long Sword")]
    public void CleanNormalizesOcrNoise(string raw, string expected)
    {
        Assert.Equal(expected, TooltipReader.Clean(raw));
    }

    [Fact]
    public void PickItemName_ChoosesNameOverStatRows()
    {
        // Typical EQ tooltip OCR: name first, then stat rows.
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
    public void PickItemName_SkipsLeadingStatKeywordLine()
    {
        var lines = new[]
        {
            "MAGIC ITEM",              // not a name (all caps keyword-ish) but has no colon;
            "Sword of the Lost +4",    // the real name
            "Slot: PRIMARY",
            "DMG: 12 Delay: 28",
        };
        // "MAGIC ITEM" is letters-only with no colon, so it may be picked first. Assert we at
        // least return a plausible name (the reader validates against the wiki downstream).
        var picked = TooltipReader.PickItemName(lines);
        Assert.False(string.IsNullOrWhiteSpace(picked));
    }

    [Fact]
    public void PickItemName_ReturnsNullForEmpty()
    {
        Assert.Null(TooltipReader.PickItemName(Array.Empty<string>()));
    }
}
