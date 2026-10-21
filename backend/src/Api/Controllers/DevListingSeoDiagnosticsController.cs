using System.Net;
using System.Text.RegularExpressions;
using Application.DTOs.Listing;
using Application.Interfaces;
using Infrastructure.Integrations.OpenAI;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

/// <summary>
/// Runs the AI rewrite over a batch of drafts and scores the output against the TikTok Shop US
/// title and listing-quality rules, without persisting anything.
///
/// The checks here are written from the platform rules rather than reused from
/// <c>ListingDescriptionAiService</c> on purpose: a measurement that shares code with the thing
/// it measures cannot catch a bug in that shared code. Judging a prompt change by reading a
/// handful of titles is how the earlier regressions slipped through, so the metrics are the
/// evidence a change is supposed to move.
///
/// Development only; every action 404s elsewhere so the endpoint does not exist in production.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/dev/seo")]
[ApiExplorerSettings(IgnoreApi = true)]
public partial class DevListingSeoDiagnosticsController : ControllerBase
{
    private const int TitleTargetMin = 150;
    private const int TitleTargetMax = 250;
    private const int TitleHardMax = 255;
    private const int TitleHardMin = 40;

    /// <summary>Search results truncate around here, so the keyword has to land inside it.</summary>
    private const int TitleVisiblePrefix = 30;

    /// <summary>Plain-text budget the description prompt is held to (body + hashtag).</summary>
    private const int DescriptionMaxLength = 2900;

    private static readonly Regex TagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex MarketplaceRegex = new(
        @"\b(?:shein|lazada|temu|amazon|aliexpress|wish|romwe|zaful|cider|shopee|alibaba|etsy|"
        + @"ebay|walmart|tiktok)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PromoRegex = new(
        @"\$\s*\d|\b\d{1,3}\s*%\s*(?:off|discount)\b|\b(?:best\s*sellers?|hot\s*sale|on\s*sale|"
        + @"free\s*shipping|new\s*arrivals?|limited\s*time|clearance|must\s*have|cheap)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Long-tail endings with almost no US search demand — wastes the use-case slot.</summary>
    private static readonly Regex WeakLongTailRegex = new(
        @"\b(?:daily|everyday|casual|summer|vacation|party|going[- ]?out)\s+wear\b|"
        + @"\blayering\s+wear\b|\bathletic\s+style\b|\bretro\s+(?:fall\s+)?outfits?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Spec-table rows shoppers never type — independent copy of the service denylist.</summary>
    private static readonly Regex SpecSheetJunkRegex = new(
        @"\bnon[- ]?(?:sheer|stretch|lined|wired)\b|"
        + @"\b(?:regular|standard)[- ]?(?:length|fit)\b|"
        + @"\b(?:slight(?:ly)?|medium|high|low)[- ]?stretch\b|"
        + @"\bslim[- ]?fit(?:ted|ting)?\b|"
        + @"\b(?:polyester|spandex|elastane|viscose|acrylic)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Invented product types — independent of the service garment-family table.</summary>
    private static readonly Regex SourceSetRegex = new(
        @"\bsets?\b|\b(?:2|two)[- ]pieces?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InventedCamiRegex = new(
        @"\b(?:cami|camisole)s?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InventedDressRegex = new(
        @"\b(?:dresses?|gowns?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CatalogYearRegex = new(
        @"\b20(?:2[0-9]|3[0-9])\b",
        RegexOptions.Compiled);

    private static readonly Regex SupplierSloganRegex = new(
        @"\b(?:perfectsummer|musthave|newin)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Symbols TikTok rejects in titles; the sanitizer is supposed to have removed them.</summary>
    private static readonly Regex DisallowedCharRegex = new(
        @"[^A-Za-z0-9 '&,\.\-/]", RegexOptions.Compiled);

    private static readonly Regex AllCapsWordRegex = new(
        @"\b[A-Z]{4,}\b", RegexOptions.Compiled);

    private static readonly Regex KeyValueRegex = new(
        @"(?m)^\s*[A-Za-z][^:\n]{1,60}:\s*\S", RegexOptions.Compiled);

    /// <summary>Words that never carry search intent, so they must not sit before the keyword.</summary>
    private static readonly HashSet<string> TitleStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "but", "a", "an", "the", "for", "in", "on", "with", "to", "of", "at",
        "by", "from", "as", "per", "via", "new", "hot", "sexy", "fashion", "fashionable",
        "trendy", "stylish", "elegant", "casual", "summer", "spring", "autumn", "fall", "winter"
    };

    /// <summary>
    /// Attributes that are a US search query on their own. Losing one the supplier advertised is
    /// the single most expensive rewrite failure, so it is scored separately from wording.
    /// </summary>
    private static readonly (string Label, Regex Pattern)[] SourceKeywords =
    [
        ("seamless", new Regex(@"\bseamless\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("high waist", new Regex(@"\bhigh[- ]?waist(?:ed)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("ribbed", new Regex(@"\bribbed\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("cropped", new Regex(@"\bcrop(?:ped|top)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("backless", new Regex(@"\bbackless\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("strapless", new Regex(@"\bstrapless\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("off shoulder", new Regex(@"\boff[- ]?(?:the[- ]?)?shoulder\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("bodycon", new Regex(@"\bbodycon\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("oversized", new Regex(@"\boversi[sz]ed\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("plus size", new Regex(@"\bplus[- ]?si[sz]e\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("maternity", new Regex(@"\bmaternity\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("mesh", new Regex(@"\bmesh\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("linen", new Regex(@"\blinen\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("denim", new Regex(@"\bdenim\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("satin", new Regex(@"\bsatin\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("velvet", new Regex(@"\bvelvet\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("sheer", new Regex(@"\bsheer\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("padded", new Regex(@"\bpadded\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("pockets", new Regex(@"\bpockets?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("floral", new Regex(@"\bfloral\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("sequin", new Regex(@"\bsequins?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase))
    ];

    /// <summary>Sections the description prompt promises; a missing one is a structure fault.</summary>
    private static readonly string[] RequiredSections =
        ["Why You Will Love It", "Product Details", "Great For"];

    private static readonly Regex HashtagTokenRegex = new(
        @"#([a-z0-9]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ColorsSpecLineRegex = new(
        @"^\s*(?:[-*•·]|–|—)?\s*(?:available\s+)?colou?rs?\s*[:\-–—]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SizesSpecLineRegex = new(
        @"^\s*(?:[-*•·]|–|—)?\s*(?:available\s+)?sizes?\s*[:\-–—]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SuperlativeRegex = new(
        @"\b(?:best|perfect|guaranteed|number\s*one|#1)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] BannedHashtagStems =
    [
        "shein", "romwe", "temu", "lazada", "aliexpress", "amazon", "tiktok", "wish",
        "freeshipping", "bestseller", "musthave", "newin", "hotsale", "flashsale", "clearance"
    ];

    private readonly ApplicationDbContext _db;
    private readonly IListingDescriptionAiService _rewrite;
    private readonly IWebHostEnvironment _env;
    private readonly OpenAiSettings _openAiSettings;

    /// <summary>Creates the SEO audit controller.</summary>
    public DevListingSeoDiagnosticsController(
        ApplicationDbContext db,
        IListingDescriptionAiService rewrite,
        IWebHostEnvironment env,
        Microsoft.Extensions.Options.IOptions<OpenAiSettings> openAiOptions)
    {
        _db = db;
        _rewrite = rewrite;
        _env = env;
        _openAiSettings = openAiOptions.Value;
    }

    /// <summary>
    /// Rewrites <paramref name="take"/> drafts and returns per-listing faults plus aggregate
    /// counts. Nothing is written back, so the same batch can be re-run after a prompt change
    /// and the two aggregates compared directly.
    /// </summary>
    [HttpPost("rewrite-audit")]
    public async Task<IActionResult> RewriteAudit(
        [FromQuery] int take = 20,
        [FromQuery] bool verbose = false,
        [FromQuery] int? seed = null,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var count = Math.Clamp(take, 1, 50);
        var drafts = await LoadAuditDraftsAsync(count, cancellationToken);

        var items = new List<object>();
        var failures = new List<object>();

        foreach (var draft in drafts)
        {
            var sourceTitle = draft.ProductName ?? string.Empty;
            var sourcePlain = ToPlainText(draft.DescriptionHtml);

            try
            {
                var result = await _rewrite.RewriteAsync(
                    new RewriteListingDescriptionRequest
                    {
                        Title = sourceTitle,
                        Description = draft.DescriptionHtml ?? string.Empty,
                        CategoryName = draft.CategoryName,
                        Seed = seed
                    },
                    cancellationToken);

                items.Add(ScoreOne(draft.Id, sourceTitle, sourcePlain, result, verbose));
            }
            catch (Exception ex)
            {
                failures.Add(new { listingId = draft.Id, error = ex.Message });
            }
        }

        return Ok(new
        {
            requested = count,
            scored = items.Count,
            failed = failures.Count,
            seed,
            failures,
            aggregate = Aggregate(items),
            items
        });
    }

    private async Task<List<AuditDraftRow>> LoadAuditDraftsAsync(
        int count,
        CancellationToken cancellationToken)
        => await _db.Listings
            .AsNoTracking()
            .Where(l => l.PageType == "draft"
                        && !l.DescriptionRewritten
                        && l.ProductName != null
                        && l.DescriptionHtml != null)
            .OrderBy(l => l.Id)
            .Take(count)
            .Select(l => new AuditDraftRow(l.Id, l.ProductName, l.DescriptionHtml, l.CategoryName))
            .ToListAsync(cancellationToken);

    private sealed record AuditDraftRow(
        Guid Id,
        string? ProductName,
        string? DescriptionHtml,
        string? CategoryName);

    private static object ScoreOne(
        Guid listingId,
        string sourceTitle,
        string sourcePlain,
        RewriteListingDescriptionResultDto result,
        bool verbose)
    {
        var title = result.Title ?? string.Empty;
        var descPlain = ToPlainText(result.DescriptionHtml);
        var descRaw = result.DescriptionHtml ?? string.Empty;
        var faults = new List<string>();

        if (title.Length > TitleHardMax) faults.Add("title-over-255");
        if (title.Length < TitleHardMin) faults.Add("title-under-40");
        var inBand = title.Length >= TitleTargetMin && title.Length <= TitleTargetMax;
        if (title.Length > TitleTargetMax && title.Length <= TitleHardMax)
        {
            faults.Add("title-over-band");
        }

        if (MarketplaceRegex.IsMatch(title)) faults.Add("title-marketplace-name");
        if (PromoRegex.IsMatch(title)) faults.Add("title-promo-wording");
        if (DisallowedCharRegex.IsMatch(title)) faults.Add("title-disallowed-char");
        if (AllCapsWordRegex.IsMatch(title)) faults.Add("title-all-caps-word");
        if (CatalogYearRegex.IsMatch(title)) faults.Add("title-catalog-year");
        if (SupplierSloganRegex.IsMatch(title)) faults.Add("title-supplier-slogan");

        var repeated = FindRepeatedWord(title);

        if (WeakLongTailRegex.IsMatch(title)) faults.Add("title-weak-long-tail");
        if (SpecSheetJunkRegex.IsMatch(title) && !SpecSheetJunkRegex.IsMatch(sourceTitle))
        {
            faults.Add("title-spec-sheet-junk");
        }

        var inventedGarment =
            (SourceSetRegex.IsMatch(title) && !SourceSetRegex.IsMatch(sourceTitle))
            || (InventedCamiRegex.IsMatch(title) && !InventedCamiRegex.IsMatch(sourceTitle))
            || (InventedDressRegex.IsMatch(title) && !InventedDressRegex.IsMatch(sourceTitle));
        if (inventedGarment)
        {
            faults.Add("title-invented-garment");
        }

        var leadBrand = FindLeadingSupplierBrand(sourceTitle, title);
        if (leadBrand is not null) faults.Add("title-leads-with-brand");

        if (!HasKeywordInVisiblePrefix(title)) faults.Add("title-filler-before-keyword");

        var dropped = SourceKeywords
            .Where(k => k.Pattern.IsMatch(sourceTitle) && !k.Pattern.IsMatch(title))
            .Select(k => k.Label)
            .ToList();
        if (dropped.Count > 0) faults.Add("title-dropped-source-attribute");

        var unsupported = FindUnsupportedTitleWords(title, sourceTitle, sourcePlain, descPlain);
        if (unsupported.Count > 0) faults.Add("title-word-not-in-source");

        if (descPlain.Length > DescriptionMaxLength) faults.Add("description-over-budget");
        if (descPlain.Length < 400) faults.Add("description-too-thin");

        var missingSections = RequiredSections
            .Where(s => !descPlain.Contains(s, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (missingSections.Count > 0) faults.Add("description-missing-section");
        if (PromoRegex.IsMatch(descPlain)) faults.Add("description-promo-wording");

        if (descPlain.Contains("Care Tips", StringComparison.OrdinalIgnoreCase)
            || descRaw.Contains(">Care Tips<", StringComparison.OrdinalIgnoreCase))
        {
            faults.Add("description-forbidden-care-tips");
        }

        if (HasColorsSpecLine(descPlain) || HasColorsSpecLine(ToPlainTextKeepLines(descRaw)))
        {
            faults.Add("description-forbidden-colors-line");
        }

        if (HasSizesSpecLine(descPlain) || HasSizesSpecLine(ToPlainTextKeepLines(descRaw)))
        {
            faults.Add("description-forbidden-sizes-line");
        }

        var descMarketplace = MarketplaceRegex.Matches(descPlain)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (descMarketplace.Count > 0) faults.Add("description-marketplace-name");

        var hashtagCount = CountHashtags(descPlain);
        if (hashtagCount == 0) faults.Add("description-missing-hashtag");
        else if (hashtagCount < 15) faults.Add("hashtag-count-low");
        else if (hashtagCount > 25) faults.Add("hashtag-count-high");
        if (HasBannedHashtag(descPlain)) faults.Add("hashtag-banned-stem");

        var primaryPhrase = InferPrimaryPhrase(title, descPlain);
        var primaryCount = CountPhraseOccurrences(descPlain, primaryPhrase);
        if (primaryPhrase.Length > 0 && (primaryCount < 4 || primaryCount > 6))
        {
            faults.Add(primaryCount < 4
                ? "description-primary-density-low"
                : "description-primary-density-high");
        }

        var searchTerms = result.SearchTerms ?? [];
        ScoreSearchTerms(searchTerms, title, descPlain, faults);

        var highlights = result.ProductHighlights ?? [];
        ScoreHighlights(highlights, sourceTitle, sourcePlain, descPlain, faults);

        var score = ComputeScore(
            faults.Distinct(StringComparer.Ordinal).Count(),
            inBand,
            hashtagCount,
            searchTerms.Count,
            highlights.Count);

        var scored = new Dictionary<string, object?>
        {
            ["listingId"] = listingId,
            ["titleLength"] = title.Length,
            ["titleInBand"] = inBand,
            ["descriptionLength"] = descPlain.Length,
            ["hashtagCount"] = hashtagCount,
            ["primaryKeywordCount"] = primaryCount,
            ["primaryPhrase"] = primaryPhrase,
            ["searchTermsCount"] = searchTerms.Count,
            ["productHighlightsCount"] = highlights.Count,
            ["score"] = score,
            ["sourceKeyValuePairs"] = KeyValueRegex.Matches(sourcePlain).Count,
            ["repeatedWord"] = repeated,
            ["leadingBrand"] = leadBrand,
            ["droppedSourceAttributes"] = dropped,
            ["unsupportedTitleWords"] = unsupported,
            ["descriptionMarketplaceHits"] = descMarketplace,
            ["missingSections"] = missingSections,
            ["faults"] = faults.Distinct(StringComparer.Ordinal).ToList()
        };

        if (verbose)
        {
            scored["sourceTitle"] = sourceTitle;
            scored["newTitle"] = title;
            scored["description"] = descPlain;
            scored["searchTerms"] = searchTerms;
            scored["productHighlights"] = highlights;
        }
        else
        {
            scored["newTitle"] = title;
        }

        return scored;
    }

    private static void ScoreSearchTerms(
        IReadOnlyList<string> terms,
        string title,
        string descPlain,
        List<string> faults)
    {
        if (terms.Count == 0)
        {
            faults.Add("search-terms-empty");
            return;
        }

        if (terms.Count < 4) faults.Add("search-terms-count-low");
        if (terms.Count > 6) faults.Add("search-terms-count-high");

        var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            var t = (term ?? string.Empty).Trim();
            if (t.Length == 0) continue;
            if (MarketplaceRegex.IsMatch(t)) faults.Add("search-terms-marketplace");
            if (PromoRegex.IsMatch(t)) faults.Add("search-terms-promo");
            var stem = Regex.Replace(t.ToLowerInvariant(), @"[^a-z0-9]+", "");
            if (stem.Length >= 4 && !stems.Add(stem))
            {
                faults.Add("search-terms-duplicate-intent");
            }
        }

        var hay = $"{title}\n{descPlain}";
        var overlap = terms.Any(t =>
            (t ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(w => w.Length >= 4
                          && hay.Contains(w, StringComparison.OrdinalIgnoreCase)));
        if (!overlap) faults.Add("search-terms-desync");
    }

    private static void ScoreHighlights(
        IReadOnlyList<string> highlights,
        string sourceTitle,
        string sourcePlain,
        string descPlain,
        List<string> faults)
    {
        if (highlights.Count == 0)
        {
            faults.Add("highlights-empty");
            return;
        }

        if (highlights.Count < 3) faults.Add("highlights-count-low");
        if (highlights.Count > 5) faults.Add("highlights-count-high");

        var hay = $"{sourceTitle}\n{sourcePlain}\n{descPlain}";
        foreach (var raw in highlights)
        {
            var h = (raw ?? string.Empty).Trim();
            if (h.Length == 0) continue;
            if (h.Length < 40) faults.Add("highlights-too-short");
            if (h.Length > 80) faults.Add("highlights-too-long");
            if (MarketplaceRegex.IsMatch(h)) faults.Add("highlights-marketplace");
            if (SuperlativeRegex.IsMatch(h)) faults.Add("highlights-superlative");
            var grounded = h.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(w => w.Length >= 4 && hay.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (!grounded) faults.Add("highlights-ungrounded");
        }
    }

    private static int ComputeScore(
        int faultCount,
        bool titleInBand,
        int hashtagCount,
        int termsCount,
        int highlightsCount)
    {
        var score = Math.Max(0, 100 - faultCount * 10);
        if (titleInBand) score = Math.Min(100, score + 5);
        if (hashtagCount is >= 15 and <= 25) score = Math.Min(100, score + 5);
        if (termsCount is >= 4 and <= 6) score = Math.Min(100, score + 5);
        if (highlightsCount is >= 3 and <= 5) score = Math.Min(100, score + 5);
        return score;
    }

    private static int CountHashtags(string text)
        => HashtagTokenRegex.Matches(text ?? string.Empty).Count;

    private static bool HasBannedHashtag(string text)
    {
        foreach (Match m in HashtagTokenRegex.Matches(text ?? string.Empty))
        {
            var body = m.Groups[1].Value.ToLowerInvariant();
            if (CatalogYearRegex.IsMatch(body)) return true;
            if (BannedHashtagStems.Any(s =>
                    body.Equals(s, StringComparison.OrdinalIgnoreCase)
                    || body.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasColorsSpecLine(string text)
    {
        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            if (ColorsSpecLineRegex.IsMatch(line.Trim())) return true;
        }

        return false;
    }

    private static bool HasSizesSpecLine(string text)
    {
        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            if (SizesSpecLineRegex.IsMatch(line.Trim())) return true;
        }

        return false;
    }

    private static string InferPrimaryPhrase(string title, string descPlain)
    {
        // First ~6 content words of the description opening, falling back to title head.
        var opening = (descPlain ?? string.Empty).Split('.', 2)[0];
        var words = opening
            .Split([' ', ',', '—', '–', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .Take(5)
            .ToList();
        if (words.Count >= 2) return string.Join(' ', words.Take(4)).ToLowerInvariant();

        return string.Join(
                ' ',
                title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4))
            .ToLowerInvariant();
    }

    private static int CountPhraseOccurrences(string haystack, string phrase)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(phrase))
        {
            return 0;
        }

        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(phrase, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += phrase.Length;
        }

        return count;
    }

    private static string ToPlainTextKeepLines(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var withBreaks = html
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</h3>", "\n", StringComparison.OrdinalIgnoreCase);
        return WebUtility.HtmlDecode(TagRegex.Replace(withBreaks, " "));
    }

    private static object Aggregate(List<object> items)
    {
        var rows = items.Cast<Dictionary<string, object?>>().ToList();
        if (rows.Count == 0) return new { note = "no rows" };

        var lengths = rows.Select(r => (int)r["titleLength"]!).ToList();
        var descLengths = rows.Select(r => (int)r["descriptionLength"]!).ToList();
        var scores = rows.Select(r => (int)r["score"]!).ToList();
        var termsCounts = rows.Select(r => (int)r["searchTermsCount"]!).ToList();
        var highlightCounts = rows.Select(r => (int)r["productHighlightsCount"]!).ToList();

        var faultCounts = rows
            .SelectMany(r => (List<string>)r["faults"]!)
            .GroupBy(f => f)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        return new
        {
            titleLengthMin = lengths.Min(),
            titleLengthAvg = Math.Round(lengths.Average(), 1),
            titleLengthMax = lengths.Max(),
            titlesInBand = rows.Count(r => (bool)r["titleInBand"]!),
            descriptionLengthAvg = Math.Round(descLengths.Average(), 0),
            avgScore = Math.Round(scores.Average(), 1),
            searchTermsAvgCount = Math.Round(termsCounts.Average(), 1),
            highlightsAvgCount = Math.Round(highlightCounts.Average(), 1),
            metaComplete = rows.Count(r =>
                (int)r["searchTermsCount"]! is >= 4 and <= 6
                && (int)r["productHighlightsCount"]! is >= 3 and <= 5),
            cleanListings = rows.Count(r => ((List<string>)r["faults"]!).Count == 0),
            total = rows.Count,
            faultCounts
        };
    }

    /// <summary>
    /// Returns a keyword used twice, counting singular and plural as one word. Adjacent repeats
    /// are already collapsed upstream, so only the non-adjacent case is interesting here.
    /// </summary>
    private static string? FindRepeatedWord(string title)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in title.Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            var bare = word.Trim('\'', '.', '&', '/');
            if (bare.Length < 3 || TitleStopWords.Contains(bare)) continue;

            var stem = bare.Length > 3 && bare.EndsWith('s') ? bare[..^1] : bare;
            if (!seen.Add(stem)) return bare;
        }

        return null;
    }

    /// <summary>
    /// Words a title may legitimately open with. They look like an unknown brand token to a
    /// first-word heuristic, but <c>Plus Size</c> and <c>Maternity</c> are keywords shoppers
    /// actually type, so leading with them is correct rather than a leaked supplier name.
    /// </summary>
    private static readonly HashSet<string> LegitimateLeadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "plus", "size", "women", "womens", "woman", "men", "mens", "man", "girls", "boys",
        "kids", "unisex", "maternity", "juniors", "ladies", "teen", "toddler", "baby"
    };

    /// <summary>
    /// Detects the supplier brand surviving at the front of the rewrite. A brand is taken as the
    /// first source-title token that is not a normal product or audience word, which is how the
    /// Aloruh and Slaydiva style prefixes present themselves.
    /// </summary>
    private static string? FindLeadingSupplierBrand(string sourceTitle, string newTitle)
    {
        var first = sourceTitle
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (first is null || first.Length < 4) return null;

        var brand = first.Trim('\'', '.', '&', '/');
        if (TitleStopWords.Contains(brand)
            || LegitimateLeadWords.Contains(brand)
            || brand.Contains('\''))
        {
            return null;
        }

        return newTitle.StartsWith(brand, StringComparison.OrdinalIgnoreCase) ? brand : null;
    }

    /// <summary>
    /// True when a real keyword lands inside the visible prefix instead of filler. Season and
    /// mood words score as filler: nobody searches for them and they eat the visible window.
    /// </summary>
    private static bool HasKeywordInVisiblePrefix(string title)
    {
        if (title.Length == 0) return false;

        var prefix = title.Length <= TitleVisiblePrefix ? title : title[..TitleVisiblePrefix];
        return prefix
            .Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('\'', '.', '&', '/'))
            .Any(w => w.Length >= 4
                      && !TitleStopWords.Contains(w)
                      && !w.EndsWith("'s", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Title words that appear in neither the source title, the source description nor the
    /// rewritten description. Those are the words a shopper cannot verify against the product.
    /// Same-product synonyms (cami/camisole, coat/outerwear, date/night) are not faults.
    /// </summary>
    private static readonly Dictionary<string, string[]> TitleWordAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["camisole"] = ["cami"],
            ["cami"] = ["camisole"],
            ["outerwear"] = ["coat", "coats", "trench", "jacket"],
            ["night"] = ["date", "dates", "nights"],
            ["tee"] = ["t-shirt", "tshirt", "shirt"],
            ["tshirt"] = ["tee", "t-shirt", "shirt"],
            ["ruffled"] = ["ruffle", "ruffles"],
            ["ruffle"] = ["ruffled", "ruffles"],
            ["tartan"] = ["plaid", "check", "checked"],
            ["check"] = ["plaid", "tartan", "checked"],
            ["gathered"] = ["ruched", "ruching"],
            ["ruched"] = ["gathered", "gather"],
            ["sundress"] = ["dress", "dresses", "mini"],
            ["knitted"] = ["knit"],
            ["knit"] = ["knitted"]
        };

    private static List<string> FindUnsupportedTitleWords(
        string title,
        string sourceTitle,
        string sourcePlain,
        string descriptionPlain)
    {
        var haystack = $"{sourceTitle}\n{sourcePlain}\n{descriptionPlain}";
        return title
            .Split([' ', ',', '-', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('\'', '.', '&'))
            .Where(w => w.Length >= 5 && !TitleStopWords.Contains(w))
            .Where(w => !w.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
            .Where(w => !TitleWordIsGrounded(w, haystack))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TitleWordIsGrounded(string word, string haystack)
    {
        if (haystack.IndexOf(TrimPlural(word), StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (!TitleWordAliases.TryGetValue(word, out var aliases))
        {
            return false;
        }

        return aliases.Any(alias =>
            haystack.IndexOf(TrimPlural(alias), StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string TrimPlural(string word) =>
        word.Length > 4 && word.EndsWith('s') ? word[..^1] : word;

    private static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var withBreaks = html
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</h3>", "\n", StringComparison.OrdinalIgnoreCase);

        var text = WebUtility.HtmlDecode(TagRegex.Replace(withBreaks, " "));
        return SpaceRegex.Replace(text.Replace('\n', ' '), " ").Trim();
    }
}
