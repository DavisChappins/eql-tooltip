using System.Net.Http;
using EqWikiOverlay.Wiki;

namespace EqWikiOverlay.Tests;

/// <summary>
/// End-to-end wiki check that hits the live eqlwiki.com API. Trait("Category","Network") so it
/// can be excluded offline.
/// </summary>
public class PipelineIntegrationTests
{
    [Fact]
    [Trait("Category", "Network")]
    public async Task EqlWikiProvider_ResolvesRealItem()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var provider = new EqlWikiProvider(http);

        var result = await provider.LookupAsync("Cloth Cap");

        Assert.True(result.Found, "Expected a wiki hit for 'Cloth Cap'");
        Assert.Equal("Cloth Cap", result.PageTitle);
        Assert.NotEmpty(result.Sections);
        // Shows acquisition sections; a "Drops From" section is present, no markup leaks.
        Assert.Contains(result.Sections, s => s.Label == "Drops From");
        Assert.DoesNotContain(result.Sections.SelectMany(s => s.Lines), l => l.Text.Contains('<'));
        Assert.StartsWith("https://eqlwiki.com/index.php?title=Cloth_Cap", result.PageUrl);
    }

    [Fact]
    [Trait("Category", "Network")]
    public async Task EqlWikiProvider_StripsUpgradeSuffixAndResolves()
    {
        // Mirrors the real Legends tooltip case: "Sword of the Lost +4" -> base page.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var provider = new EqlWikiProvider(http);

        // The +N is stripped by TooltipReader before lookup, so query the base name.
        var result = await provider.LookupAsync("Sword of the Lost");

        Assert.True(result.Found);
        Assert.Equal("Sword of the Lost", result.PageTitle);
    }
}
