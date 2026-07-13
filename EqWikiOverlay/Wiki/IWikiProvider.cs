using EqWikiOverlay.Models;

namespace EqWikiOverlay.Wiki;

/// <summary>
/// A source of wiki information for an item name. Source-agnostic so additional wikis
/// (eqlwiki, gnollguard, p99, ...) can be plugged in behind the same interface.
/// </summary>
public interface IWikiProvider
{
    /// <summary>Short key identifying this source, e.g. "eqlwiki".</summary>
    string Source { get; }

    /// <summary>
    /// Resolves an item name to a wiki page and returns a summary. Returns a
    /// <see cref="WikiItem"/> with <see cref="WikiItem.Found"/> == false if nothing matched.
    /// </summary>
    Task<WikiItem> LookupAsync(string itemName, CancellationToken ct = default);
}
