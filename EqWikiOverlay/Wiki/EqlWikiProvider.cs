using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using EqWikiOverlay.Models;

namespace EqWikiOverlay.Wiki;

/// <summary>
/// Wiki provider backed by a MediaWiki Action API (default: eqlwiki.com).
///
/// Resolves an item to a page via list=search, then fetches the page's RENDERED HTML
/// (action=parse&amp;prop=text) and extracts the "acquisition" sections the user cares about:
/// Drops From, Sold by, Related quests, Player crafted, Tradeskill recipes. These are emitted by
/// the {{Itempage}} template as &lt;h2 id="..."&gt; headings, each followed by a list/paragraph, so
/// the raw wikitext does not contain them — the rendered HTML does. In-game stats (DMG/slot/etc.)
/// are intentionally NOT shown, since the player already sees those in EQ's own tooltip.
/// </summary>
public sealed class EqlWikiProvider : IWikiProvider
{
    private readonly HttpClient _http;
    private readonly string _apiUrl;

    public string Source { get; }

    /// <summary>Sections to extract, keyed by the heading id in the rendered HTML, with a display label.</summary>
    private static readonly (string Id, string Label)[] Sections =
    {
        ("Drops_From", "Drops From"),
        ("Sold_by", "Sold by"),
        ("Related_quests", "Related quests"),
        ("Player_crafted", "Player crafted"),
        ("Tradeskill_recipes", "Tradeskill recipes"),
    };

    public EqlWikiProvider(HttpClient http, string apiUrl = "https://eqlwiki.com/api.php", string source = "eqlwiki")
    {
        _http = http;
        _apiUrl = apiUrl;
        Source = source;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "EqWikiOverlay/1.0 (personal EverQuest reference companion)");
        }
    }

    public async Task<WikiItem> LookupAsync(string itemName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return WikiItem.NotFound(Source, itemName);

        // Try the raw OCR text first, then OCR-error-corrected variants (e.g. "Moming"->"Morning").
        // MediaWiki search returns nothing for badly garbled words, so correcting BEFORE searching
        // is what recovers them.
        (string Title, int PageId)? hit = null;
        foreach (var variant in OcrVariants(itemName))
        {
            hit = await SearchAsync(variant, ct).ConfigureAwait(false);
            if (hit is not null)
                break;
        }
        if (hit is null)
            return WikiItem.NotFound(Source, itemName);

        var html = await FetchRenderedHtmlAsync(hit.Value.PageId, ct).ConfigureAwait(false);
        var sections = html is null ? new List<WikiSection>() : BuildSections(html);

        return new WikiItem
        {
            Source = Source,
            CanonicalName = itemName,
            PageTitle = hit.Value.Title,
            PageId = hit.Value.PageId,
            PageUrl = BuildPageUrl(hit.Value.Title),
            Sections = sections
        };
    }

    /// <summary>
    /// Yields the query plus OCR-error-corrected variants to try in order. EQ's small font makes
    /// the OCR engine confuse certain shapes; correcting them lets the wiki search find the page.
    /// </summary>
    internal static IEnumerable<string> OcrVariants(string q)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

        IEnumerable<string> Candidates()
        {
            yield return q;

            // Fix simple 1:1 confusions.
            var s = q.Replace('q', 'g').Replace('0', 'o');
            yield return s;

            // EQ's green title font reads 'h' as ')' ("Cloth" -> "Clot)"); recover it (and drop a
            // stray '(' from the same confusion).
            if (q.IndexOf(')') >= 0 || q.IndexOf('(') >= 0)
                yield return s.Replace(")", "h").Replace("(", "");

            // The most common EQ OCR error: "rn" is read as "m" ("Morning" -> "Moming").
            // Expand each 'm' to 'rn' one at a time (covers a single-word slip) and all at once.
            yield return s.Replace("m", "rn");
            yield return q.Replace("m", "rn");

            int idx = 0;
            while ((idx = s.IndexOf('m', idx)) >= 0)
            {
                yield return s[..idx] + "rn" + s[(idx + 1)..];
                idx++;
            }
        }

        foreach (var c in Candidates())
        {
            var n = Norm(c);
            if (n.Length > 0 && seen.Add(n))
                yield return n;
        }
    }

    private async Task<(string Title, int PageId)?> SearchAsync(string query, CancellationToken ct)
    {
        // Pull several candidates and pick the one closest to the (possibly OCR-garbled) query by
        // edit distance. This recovers common OCR letter-swaps, e.g. "sbletto" -> "Stiletto".
        var url = $"{_apiUrl}?action=query&list=search&format=json&srlimit=8&srsearch={Uri.EscapeDataString(query)}";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
        if (doc is null)
            return null;

        if (!doc.RootElement.TryGetProperty("query", out var q) ||
            !q.TryGetProperty("search", out var search) ||
            search.GetArrayLength() == 0)
        {
            return null;
        }

        (string Title, int PageId)? best = null;
        double bestScore = double.MaxValue;
        int index = 0;
        foreach (var hit in search.EnumerateArray())
        {
            var title = hit.GetProperty("title").GetString();
            if (title is null) { index++; continue; }
            var pageId = hit.GetProperty("pageid").GetInt32();

            // Normalized edit distance (0 = identical). Slightly favor earlier (higher-ranked) hits.
            double dist = NormalizedEditDistance(query, title) + index * 0.02;
            if (dist < bestScore)
            {
                bestScore = dist;
                best = (title, pageId);
            }
            index++;
        }

        return best;
    }

    /// <summary>Levenshtein distance normalized to [0,1] by the longer string length, case-insensitive.</summary>
    internal static double NormalizedEditDistance(string a, string b)
    {
        a = a.Trim().ToLowerInvariant();
        b = b.Trim().ToLowerInvariant();
        if (a.Length == 0 && b.Length == 0) return 0;
        int max = Math.Max(a.Length, b.Length);
        if (max == 0) return 0;

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return (double)prev[b.Length] / max;
    }

    private async Task<string?> FetchRenderedHtmlAsync(int pageId, CancellationToken ct)
    {
        var url = $"{_apiUrl}?action=parse&format=json&prop=text&disablelimitreport=1&pageid={pageId}";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
        if (doc is null)
            return null;

        if (doc.RootElement.TryGetProperty("parse", out var parse) &&
            parse.TryGetProperty("text", out var text) &&
            text.TryGetProperty("*", out var star))
        {
            return star.GetString();
        }
        return null;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private string BuildPageUrl(string title) =>
        _apiUrl.Replace("api.php", "index.php", StringComparison.OrdinalIgnoreCase) +
        "?title=" + Uri.EscapeDataString(title.Replace(' ', '_'));

    /// <summary>
    /// Builds the acquisition-focused summary from rendered page HTML: for each target section,
    /// grab the content between its &lt;h2 id&gt; and the next &lt;h2&gt;, strip tags, and label it.
    /// </summary>
    internal static List<WikiSection> BuildSections(string html)
    {
        var result = new List<WikiSection>();

        foreach (var (id, label) in Sections)
        {
            var body = ExtractSectionById(html, id);
            if (body is null)
                continue;

            // "Drops From" carries structure worth keeping: a <p> zone followed by <ul><li> mobs.
            var lines = id == "Drops_From"
                ? ParseDropsLines(body)
                : ParsePlainLines(body, label);

            if (lines.Count == 0)
                continue;

            result.Add(new WikiSection
            {
                Label = label,
                Lines = lines,
                IsEmptyTemplate = lines.All(l => l.Kind == LineKind.Boilerplate)
            });
        }

        return result;
    }

    /// <summary>
    /// Parses the Drops From body preserving zone/mob structure: text in &lt;p&gt; is a zone
    /// (sub-heading), each &lt;li&gt; is a mob under the most recent zone.
    /// </summary>
    private static List<WikiLine> ParseDropsLines(string body)
    {
        var lines = new List<WikiLine>();

        // Walk the body, emitting a Zone for <p>...</p> blocks and a Mob for each <li>...</li>.
        var token = new Regex(@"<p\b[^>]*>(?<p>.*?)</p>|<li\b[^>]*>(?<li>.*?)</li>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in token.Matches(body))
        {
            if (m.Groups["p"].Success)
            {
                var zone = CleanInline(m.Groups["p"].Value);
                if (zone.Length > 0)
                    lines.Add(new WikiLine { Text = zone, Kind = LineKind.Zone });
            }
            else if (m.Groups["li"].Success)
            {
                var mob = CleanInline(m.Groups["li"].Value);
                if (mob.Length > 0)
                    lines.Add(new WikiLine { Text = mob, Kind = IsBoilerplate(mob) ? LineKind.Boilerplate : LineKind.Mob });
            }
        }

        // Fallback: if the structure wasn't as expected, use flattened plain lines.
        if (lines.Count == 0)
            return ParsePlainLines(body, "Drops From");

        return lines;
    }

    /// <summary>Flattens a section body to plain (or boilerplate) lines.</summary>
    private static List<WikiLine> ParsePlainLines(string body, string label)
    {
        var text = CleanMarkup(body).Trim();
        if (text.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            text = text[label.Length..].TrimStart('\n', ' ', '•');

        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.TrimStart('•', ' ').Trim())
            .Where(l => l.Length > 0)
            .Select(l => new WikiLine { Text = l, Kind = IsBoilerplate(l) ? LineKind.Boilerplate : LineKind.Plain })
            .ToList();
    }

    /// <summary>Cleans inline HTML (links, bold) from a single element's inner content.</summary>
    private static string CleanInline(string s)
    {
        var t = Regex.Replace(s, @"<[^>]+>", "");
        t = HttpUtility.HtmlDecode(t);
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    /// <summary>Recognizes the template's "nothing here" filler so the UI can de-emphasize it.</summary>
    internal static bool IsBoilerplate(string line)
    {
        var l = line.Trim();
        return l.StartsWith("This item ", StringComparison.OrdinalIgnoreCase) &&
               (l.Contains("no related quests", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("cannot be purchased", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("not crafted by players", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("not used in player tradeskills", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("does not drop", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the raw HTML between the &lt;h2 id="sectionId"&gt; heading and the next &lt;h2&gt;
    /// (or end of document). Null if the section heading isn't present.
    /// </summary>
    internal static string? ExtractSectionById(string html, string sectionId)
    {
        // Match the <h2 ... id="sectionId" ...> tag regardless of attribute order.
        var headingRegex = new Regex(
            @"<h2\b[^>]*\bid\s*=\s*""" + Regex.Escape(sectionId) + @"""[^>]*>",
            RegexOptions.IgnoreCase);

        var m = headingRegex.Match(html);
        if (!m.Success)
            return null;

        // Skip past the end of the heading: past the closing </h2> and, if present, the wrapping
        // </div> of <div class="mw-heading">. This keeps the heading text out of the body.
        int start = m.Index + m.Length;
        var afterH2 = Regex.Match(html[start..], @"</h2>\s*(</div>)?", RegexOptions.IgnoreCase);
        if (afterH2.Success)
            start += afterH2.Index + afterH2.Length;

        // Content runs until the next section heading (its wrapping div or the <h2>) or the end.
        var next = Regex.Match(html[start..],
            @"<div\b[^>]*class=""[^""]*mw-heading[^""]*""|<h2\b", RegexOptions.IgnoreCase);
        int len = next.Success ? next.Index : html.Length - start;
        return html.Substring(start, len);
    }

    /// <summary>Strips HTML/wiki markup down to readable text with bullet lines.</summary>
    internal static string CleanMarkup(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";

        var t = s;

        // Drop MediaWiki section-edit spans and heading residue.
        t = Regex.Replace(t, @"<span[^>]*class=""[^""]*mw-editsection[^""]*""[^>]*>.*?</span>", "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // List items -> bullet lines.
        t = Regex.Replace(t, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"</li>", "", RegexOptions.IgnoreCase);

        // Block elements -> newlines.
        t = Regex.Replace(t, @"</?(p|ul|ol|div|br|tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"</?(td|th)[^>]*>", " ", RegexOptions.IgnoreCase);

        // Remaining tags gone.
        t = Regex.Replace(t, @"<[^>]+>", "");

        t = HttpUtility.HtmlDecode(t);

        // Whitespace tidy.
        t = t.Replace("\r", "");
        t = Regex.Replace(t, @"[ \t]+", " ");
        t = Regex.Replace(t, @" *\n *", "\n");
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        // Remove empty bullets.
        t = Regex.Replace(t, @"(?m)^•\s*$", "");
        return t.Trim();
    }
}
