using System.Text.Json;
using System.Text.Json.Serialization;

namespace EqWikiOverlay.Models;

/// <summary>How a line should be presented in the panel.</summary>
public enum LineKind
{
    Plain,   // ordinary line
    Zone,    // a location grouping (e.g. "Najena") — shown as a sub-heading
    Mob,     // an entry under a zone (e.g. "Unbound Flame") — shown indented with a bullet
    Boilerplate // template filler ("This item has no ...") — de-emphasized
}

/// <summary>A single presented line with its kind.</summary>
public sealed class WikiLine
{
    public required string Text { get; init; }
    public LineKind Kind { get; init; } = LineKind.Plain;
}

/// <summary>One labeled section of an item page (e.g. "Drops From").</summary>
public sealed class WikiSection
{
    public required string Label { get; init; }

    /// <summary>The lines of the section, typed so the UI can style zone vs. mob vs. filler.</summary>
    public List<WikiLine> Lines { get; init; } = new();

    /// <summary>
    /// True when the section only contains a boilerplate "nothing here" message
    /// (e.g. "This item has no related quests."). The UI renders these de-emphasized.
    /// </summary>
    public bool IsEmptyTemplate { get; init; }
}

/// <summary>
/// A resolved wiki result for one item, ready to render in a panel and cache.
/// </summary>
public sealed class WikiItem
{
    public required string Source { get; init; }
    public required string CanonicalName { get; init; }
    public string? PageTitle { get; init; }
    public int? PageId { get; init; }
    public string? PageUrl { get; init; }

    /// <summary>Structured, styled sections for the panel.</summary>
    public List<WikiSection> Sections { get; init; } = new();

    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool Found => PageId is not null || Sections.Count > 0;

    // ---- cache (de)serialization of the sections list ----
    private static readonly JsonSerializerOptions JsonOpts =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string SectionsJson => JsonSerializer.Serialize(Sections, JsonOpts);

    public static List<WikiSection> SectionsFromJson(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new List<WikiSection>()
            : JsonSerializer.Deserialize<List<WikiSection>>(json, JsonOpts) ?? new();

    public static WikiItem NotFound(string source, string name) => new()
    {
        Source = source,
        CanonicalName = name
    };
}
