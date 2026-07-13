using System.Linq;
using EqWikiOverlay.Wiki;

namespace EqWikiOverlay.Tests;

public class EqlWikiProviderTests
{
    private const string RenderedHtml = """
        <div class="mw-parser-output">
        <p>Some intro text.</p>
        <div class="mw-heading mw-heading2"><h2 id="Drops_From">Drops From</h2></div>
        <p><a href="/Najena" title="Najena">Najena</a></p>
        <ul><li><a href="/Lost_Crusader">Lost Crusader</a></li></ul>
        <div class="mw-heading mw-heading2"><h2 id="Sold_by">Sold by</h2></div>
        <ul class="esec"><li>This item cannot be purchased from merchants.</li></ul>
        <div class="mw-heading mw-heading2"><h2 id="Related_quests">Related quests</h2></div>
        <ul class="esec"><li>This item has no related quests.</li></ul>
        <div class="mw-heading mw-heading2"><h2 id="Player_crafted">Player crafted</h2></div>
        <ul class="esec"><li>This item is not crafted by players.</li></ul>
        <div class="mw-heading mw-heading2"><h2 id="Tradeskill_recipes">Tradeskill recipes</h2></div>
        <ul class="esec"><li>This item is not used in player tradeskills.</li></ul>
        </div>
        """;

    [Fact]
    public void ExtractSection_GetsDropsList()
    {
        var body = EqlWikiProvider.ExtractSectionById(RenderedHtml, "Drops_From");
        Assert.NotNull(body);
        Assert.Contains("Najena", body);
        Assert.Contains("Lost Crusader", body);
        Assert.DoesNotContain("merchants", body); // no bleed into next section
    }

    [Fact]
    public void BuildSections_ParsesDropsWithZoneAndMobKinds()
    {
        var sections = EqlWikiProvider.BuildSections(RenderedHtml);
        var drops = sections.Single(s => s.Label == "Drops From");

        Assert.False(drops.IsEmptyTemplate);
        // Najena is the zone (from <p>), Lost Crusader is a mob (from <li>).
        Assert.Contains(drops.Lines, l => l.Text == "Najena" && l.Kind == EqWikiOverlay.Models.LineKind.Zone);
        Assert.Contains(drops.Lines, l => l.Text == "Lost Crusader" && l.Kind == EqWikiOverlay.Models.LineKind.Mob);
    }

    [Fact]
    public void BuildSections_FlagsBoilerplateSectionsEmpty()
    {
        var sections = EqlWikiProvider.BuildSections(RenderedHtml);

        Assert.True(sections.Single(s => s.Label == "Sold by").IsEmptyTemplate);
        Assert.True(sections.Single(s => s.Label == "Related quests").IsEmptyTemplate);
        Assert.True(sections.Single(s => s.Label == "Player crafted").IsEmptyTemplate);
        Assert.True(sections.Single(s => s.Label == "Tradeskill recipes").IsEmptyTemplate);
    }

    [Fact]
    public void BuildSections_NoDuplicateLabelInLines()
    {
        var drops = EqlWikiProvider.BuildSections(RenderedHtml).Single(s => s.Label == "Drops From");
        Assert.DoesNotContain(drops.Lines, l => l.Text.Equals("Drops From", System.StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("This item has no related quests.")]
    [InlineData("This item cannot be purchased from merchants.")]
    [InlineData("This item is not crafted by players.")]
    [InlineData("This item is not used in player tradeskills.")]
    public void IsBoilerplate_DetectsFiller(string line)
    {
        Assert.True(EqlWikiProvider.IsBoilerplate(line));
    }

    [Fact]
    public void IsBoilerplate_RealDataIsNotFiller()
    {
        Assert.False(EqlWikiProvider.IsBoilerplate("Najena"));
        Assert.False(EqlWikiProvider.IsBoilerplate("Lost Crusader"));
    }

    [Theory]
    [InlineData("sbletto", "Stiletto")]
    [InlineData("prisbne", "Pristine")]
    [InlineData("Cloth Cap", "Cloth Cap")]
    public void NormalizedEditDistance_RanksClosestBest(string ocr, string candidate)
    {
        double good = EqlWikiProvider.NormalizedEditDistance(ocr, candidate);
        double bad = EqlWikiProvider.NormalizedEditDistance(ocr, "Flowing Black Silk Sash");
        Assert.True(good < bad, $"{candidate} ({good}) should beat unrelated ({bad})");
    }
}
