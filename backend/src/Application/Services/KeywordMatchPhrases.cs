using Application.DTOs.ProductOpportunity;

namespace Application.Services;

/// <summary>
/// Split a keyword into content tokens. Gate uses at most 2 demand modifiers + garment
/// (coverage ceil(N/2)), not AND-all. Leftover modifiers score only.
/// Keep in sync with frontend/browser-extension keywordMatchPhrases.ts.
/// </summary>
public static class KeywordMatchPhrases
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "plus", "size", "petite", "tall", "curvy", "women", "womens", "woman", "woman's",
        "for", "the", "a", "an", "and", "with", "in", "of", "to", "at", "on",
        "ladies", "lady", "girl", "girls"
    };

    private static readonly HashSet<string> GenericShould = new(StringComparer.OrdinalIgnoreCase)
    {
        "casual", "elegant", "chic", "cute", "sexy", "nice", "trendy", "holiday", "party",
        "beach", "vacation", "school", "christmas", "summer", "spring", "fall", "autumn",
        "winter", "new", "fashion", "comfortable", "loose", "fitted", "slim", "oversized",
        "plus", "size", "women", "womens"
    };

    /// <summary>Product-type nouns — missing this family is the wrong category.</summary>
    private static readonly HashSet<string> GarmentNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "dress", "dresses", "gown", "sundress",
        "top", "tops", "shirt", "shirts", "tee", "t-shirt", "tshirt", "blouse", "blouses",
        "pant", "pants", "trouser", "trousers", "jean", "jeans",
        "skirt", "skirts", "shorts",
        "set", "sets", "sweater", "sweaters", "jumper", "jumpers",
        "jacket", "jackets", "coat", "coats", "blazer", "blazers",
        "vest", "vests", "waistcoat", "waistcoats",
        "romper", "rompers", "jumpsuit", "jumpsuits", "bodysuit", "bodysuits",
        "hoodie", "hoodies", "cardigan", "cardigans",
        "legging", "leggings", "overall", "overalls", "dungaree", "dungarees",
        "pinafore", "pinafores", "cami", "camisole", "tank",
        "kimono", "poncho", "trench", "parka",
        "swimsuit", "bikini", "coverup", "cover-up"
    };

    private static readonly HashSet<string> LengthTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "midi", "mini", "maxi", "knee-length", "ankle", "tea-length",
        "midi-length", "mid-length", "floor-length", "full-length"
    };

    /// <summary>Material that makes a different product (denim vest ≠ any vest).</summary>
    private static readonly HashSet<string> MaterialTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "denim", "linen", "satin", "silk", "mesh", "leather", "lace", "knit", "velvet",
        "corduroy", "chiffon", "fleece", "wool", "sequin", "sequined", "crochet", "tweed",
        "gauze", "organza", "terry"
    };

    /// <summary>Print/motif that makes a different product.</summary>
    private static readonly HashSet<string> PrintTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "floral", "plaid", "leopard", "zebra", "snake", "camo", "camouflage", "stripe",
        "striped", "gingham", "paisley", "skeleton", "skull", "x-ray", "xray", "polka",
        "checkered", "tie-dye", "tiedye", "cow", "marble", "geometric", "graphic", "animal"
    };

    /// <summary>Silhouette/neckline that makes a different product.</summary>
    private static readonly HashSet<string> SilhouetteTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "off-shoulder", "strapless", "halter", "wrap", "a-line", "aline", "bodycon",
        "empire", "peplum", "one-shoulder", "bardot", "sweetheart", "cowl", "square-neck",
        "v-neck", "turtleneck", "collared", "asymmetric", "asymmetrical", "mermaid",
        "sheath", "babydoll", "boat", "scoop"
    };

    /// <summary>Hardware — ranking only; not counted in the coverage gate N.</summary>
    private static readonly HashSet<string> HardwareTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "button", "buttons", "placket", "pocket", "pockets", "zipper", "zip", "buckle",
        "belt", "belts", "hook", "stud", "rivet", "snap", "snaps"
    };

    /// <summary>Style era — ranking only; a denim vest is still that vest without "retro".</summary>
    private static readonly HashSet<string> StyleTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "retro", "vintage", "boho", "western", "preppy", "grunge", "y2k", "gothic",
        "romantic", "modest", "cottage", "cottagecore"
    };

    private static readonly string[][] SynonymGroups =
    [
        ["denim", "jean", "jeans", "denims"],
        ["dress", "dresses", "gown", "sundress"],
        [
            "vest dress", "pinafore", "pinafore dress", "jumper dress", "overall dress",
            "dungaree", "dungaree dress", "dungarees"
        ],
        ["vest", "vests", "waistcoat", "waistcoats"],
        ["skirt", "skirts"],
        ["pant", "pants", "trouser", "trousers"],
        ["a-line", "aline", "a line"],
        ["retro", "vintage", "retro-style", "retro style"],
        ["floral", "floral print", "flower print", "florals"],
        ["sleeveless", "sleeve-less", "no sleeve", "no sleeves", "without sleeves"],
        [
            "off-shoulder", "off shoulder", "off the shoulder", "off-the-shoulder", "bardot"
        ],
        ["ruffled", "ruffle", "ruffles"],
        ["midi", "midi length", "mid-length", "mid length"]
    ];

    /// <summary>Shopper-demand modifiers on the title gate — leftover distinctive still scores.</summary>
    private const int MaxDemandDistinctive = 2;

    /// <summary>Stamp must/should/minDistinctiveHits on research items.</summary>
    public static void Apply(ShopKeywordResearchDto dto)
    {
        ApplyList(dto.ListNow);
        ApplyList(dto.Upcoming);
    }

    /// <summary>Stamp must/should/minDistinctiveHits on a saved-keyword row.</summary>
    public static void Apply(SavedKeywordItemDto item, IEnumerable<string>? mustTokens = null)
    {
        var set = FromKeyword(item.Keyword, mustTokens);
        item.MustPhrases = set.Must;
        item.ShouldPhrases = set.Should;
        item.MinDistinctiveHits = set.MinDistinctiveHits;
    }

    /// <summary>
    /// Content words of a keyword (stopwords dropped). Used for AI tok= and sanitize.
    /// </summary>
    public static List<string> ContentTokens(string? keyword)
    {
        var norm = ShopKeywordResearchSuggest.NormalizeDisplay(keyword);
        if (norm.Length == 0)
        {
            return [];
        }

        return norm
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !Stopwords.Contains(t))
            .ToList();
    }

    /// <summary>
    /// Demand distinctive (max 2) + primary garment. Leftover modifiers go to should.
    /// Gate threshold = <see cref="MinDistinctiveHitsFor"/> on the capped demand set.
    /// <paramref name="mustTokens"/> is ignored — the gate must not depend on AI drift.
    /// </summary>
    public static KeywordMatchPhraseSet FromKeyword(string? keyword, IEnumerable<string>? mustTokens = null)
    {
        var tokens = ContentTokens(keyword);
        if (tokens.Count == 0)
        {
            return KeywordMatchPhraseSet.Empty;
        }

        _ = mustTokens;
        var garment = tokens.LastOrDefault(IsGarment);
        var demand = PickDemandDistinctive(tokens);
        var mustSet = new HashSet<string>(demand, StringComparer.OrdinalIgnoreCase);
        if (garment is not null)
        {
            mustSet.Add(garment);
        }

        var mustRaw = tokens
            .Where(t => mustSet.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var shouldRaw = tokens
            .Where(t => !GenericShould.Contains(t) && !mustSet.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new KeywordMatchPhraseSet(
            mustRaw.Select(ToPhrase).ToList(),
            shouldRaw.Select(ToPhrase).ToList(),
            MinDistinctiveHitsFor(demand.Count));
    }

    /// <summary>Keep AI tokens that are content tokens or two adjacent tokens in the keyword.</summary>
    public static List<string> SanitizeMustTokens(string? keyword, IEnumerable<string>? raw)
        => SanitizeMustTokens(ContentTokens(keyword), raw);

    /// <summary>ceil(N/2) distinctive hits required; 0 when N is 0.</summary>
    public static int MinDistinctiveHitsFor(int distinctiveCount)
        => distinctiveCount <= 0 ? 0 : (distinctiveCount + 1) / 2;

    /// <summary>
    /// Coverage gate: primary garment (if present) plus at least
    /// <see cref="KeywordMatchPhraseSet.MinDistinctiveHits"/> distinctive phrases.
    /// Empty must → true (caller may keep a legacy 1-token gate).
    /// </summary>
    public static bool PassesCoverage(
        string? name,
        string? slug,
        string? cateName,
        KeywordMatchPhraseSet set)
    {
        if (set.Must.Count == 0)
        {
            return true;
        }

        var title = TitleTokens(name, slug, cateName);
        var garments = set.Must.Where(p => IsGarment(p.Phrase)).ToList();
        if (garments.Count > 0 && !garments.Any(p => PhraseHits(title, p)))
        {
            return false;
        }

        var distinctiveHits = set.Must.Count(p => !IsGarment(p.Phrase) && PhraseHits(title, p));
        return distinctiveHits >= set.MinDistinctiveHits;
    }

    /// <summary>Alias of <see cref="PassesCoverage"/>.</summary>
    public static bool PassesMust(
        string? name,
        string? slug,
        string? cateName,
        KeywordMatchPhraseSet set)
        => PassesCoverage(name, slug, cateName, set);

    /// <summary>
    /// Title score 0–1 = matched weight / important weight.
    /// Garment + unclassified distinctive = 2; length = 1.5; style/hardware = 1.
    /// Empty → 0.
    /// </summary>
    public static double TitleScore(
        string? name,
        string? slug,
        string? cateName,
        IReadOnlyList<KeywordMatchPhraseDto>? must,
        IReadOnlyList<KeywordMatchPhraseDto>? should = null)
    {
        var phrases = ConcatPhrases(must, should);
        if (phrases.Count == 0)
        {
            return 0;
        }

        var title = TitleTokens(name, slug, cateName);
        var total = 0d;
        var hit = 0d;
        foreach (var phrase in phrases)
        {
            var weight = PhraseWeight(phrase.Phrase);
            total += weight;
            if (PhraseHits(title, phrase))
            {
                hit += weight;
            }
        }

        return total <= 0 ? 0 : hit / total;
    }

    /// <summary>Whether one phrase (canonical or any synonym) hits the title token set.</summary>
    public static bool PhraseHits(ISet<string> titleTokens, KeywordMatchPhraseDto phrase)
    {
        foreach (var alt in phrase.AnyOf ?? [])
        {
            var parts = MatchParts(alt);
            if (parts.Count == 0)
            {
                continue;
            }

            if (parts.All(p => TokenInTitle(titleTokens, p)))
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplyList(List<ShopKeywordResearchItemDto> items)
    {
        foreach (var item in items)
        {
            var set = FromKeyword(item.Keyword, item.MustTokens);
            item.MustPhrases = set.Must;
            item.ShouldPhrases = set.Should;
            item.MustTokens = set.Must.Select(p => p.Phrase).ToList();
            item.MinDistinctiveHits = set.MinDistinctiveHits;
        }
    }

    private static KeywordMatchPhraseDto ToPhrase(string phrase)
        => new()
        {
            Phrase = phrase,
            AnyOf = AlternativesFor(phrase)
        };

    private static List<string> AlternativesFor(string phrase)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { phrase };
        foreach (var group in SynonymGroups)
        {
            if (!group.Any(m => string.Equals(m, phrase, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var m in group)
            {
                set.Add(m);
            }
        }

        var rest = set
            .Where(x => !string.Equals(x, phrase, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        return [phrase, .. rest];
    }

    private static List<string> SanitizeMustTokens(List<string> tokens, IEnumerable<string>? raw)
    {
        if (raw is null || tokens.Count == 0)
        {
            return [];
        }

        var allowed = AllowedMustKeys(tokens);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw)
        {
            var norm = ShopKeywordResearchSuggest.NormalizeDisplay(item);
            if (norm.Length == 0 || !allowed.TryGetValue(norm, out var canonical))
            {
                continue;
            }

            if (!seen.Add(canonical))
            {
                continue;
            }

            result.Add(canonical);
        }

        return result;
    }

    private static Dictionary<string, string> AllowedMustKeys(List<string> tokens)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string alias, string canonical)
        {
            if (alias.Length == 0 || canonical.Length == 0)
            {
                return;
            }

            map.TryAdd(alias, canonical);
        }

        var keys = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            keys.Add(tokens[i]);
            Add(tokens[i], tokens[i]);
            if (i + 1 >= tokens.Count)
            {
                continue;
            }

            var pair = $"{tokens[i]} {tokens[i + 1]}";
            keys.Add(pair);
            Add(pair, pair);
        }

        foreach (var key in keys)
        {
            foreach (var group in SynonymGroups)
            {
                if (!group.Any(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (var member in group)
                {
                    Add(member, key);
                }
            }
        }

        return map;
    }

    private static bool IsGarment(string token) => GarmentNouns.Contains(token);

    private static bool IsLength(string token) => LengthTokens.Contains(token);

    private static bool IsDistinctive(string token)
        => !GenericShould.Contains(token)
            && !IsGarment(token)
            && !HardwareTokens.Contains(token)
            && !StyleTokens.Contains(token);

    private static bool IsHeadline(string token)
        => IsDistinctive(token)
            && !IsLength(token)
            && !MaterialTokens.Contains(token)
            && !PrintTokens.Contains(token)
            && !SilhouetteTokens.Contains(token);

    /// <summary>
    /// At most <see cref="MaxDemandDistinctive"/> modifiers, priority
    /// material → print → silhouette → headline construction → length.
    /// </summary>
    private static List<string> PickDemandDistinctive(List<string> tokens)
    {
        List<string>[] buckets =
        [
            tokens.Where(t => MaterialTokens.Contains(t)).ToList(),
            tokens.Where(t => PrintTokens.Contains(t)).ToList(),
            tokens.Where(t => SilhouetteTokens.Contains(t)).ToList(),
            tokens.Where(IsHeadline).ToList(),
            tokens.Where(IsLength).ToList()
        ];
        var picked = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in buckets)
        {
            foreach (var token in bucket)
            {
                if (picked.Count >= MaxDemandDistinctive)
                {
                    return picked;
                }

                if (!seen.Add(token))
                {
                    continue;
                }

                picked.Add(token);
            }
        }

        return picked;
    }

    private static double PhraseWeight(string? phrase)
    {
        var token = (phrase ?? string.Empty).Trim();
        if (token.Length == 0)
        {
            return 0;
        }

        if (IsGarment(token))
        {
            return 2;
        }

        if (IsLength(token))
        {
            return 1.5;
        }

        if (HardwareTokens.Contains(token) || StyleTokens.Contains(token))
        {
            return 1;
        }

        if (MaterialTokens.Contains(token) || PrintTokens.Contains(token) || SilhouetteTokens.Contains(token))
        {
            return 2;
        }

        return 2;
    }

    private static List<KeywordMatchPhraseDto> ConcatPhrases(
        IReadOnlyList<KeywordMatchPhraseDto>? must,
        IReadOnlyList<KeywordMatchPhraseDto>? should)
    {
        var list = new List<KeywordMatchPhraseDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in (must ?? []).Concat(should ?? []))
        {
            var key = (p.Phrase ?? string.Empty).Trim();
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            list.Add(p);
        }

        return list;
    }

    private static HashSet<string> TitleTokens(string? name, string? slug, string? cateName)
    {
        var hay = ShopKeywordResearchSuggest.NormalizeDisplay($"{name} {slug} {cateName}");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in hay.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            set.Add(token);
            if (!token.Contains('-', StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var part in token.Split('-', StringSplitOptions.RemoveEmptyEntries))
            {
                set.Add(part);
            }
        }

        return set;
    }

    private static List<string> MatchParts(string phrase)
    {
        var norm = ShopKeywordResearchSuggest.NormalizeDisplay(phrase).Replace('-', ' ');
        while (norm.Contains("  ", StringComparison.Ordinal))
        {
            norm = norm.Replace("  ", " ", StringComparison.Ordinal);
        }

        return norm.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static bool TokenInTitle(ISet<string> title, string token)
    {
        if (title.Contains(token))
        {
            return true;
        }

        foreach (var group in SynonymGroups)
        {
            if (!group.Any(m => string.Equals(m, token, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var m in group)
            {
                var parts = MatchParts(m);
                if (parts.Count == 1 && title.Contains(parts[0]))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>Must (coverage chips) + should (score) + distinctive-hit floor.</summary>
public sealed class KeywordMatchPhraseSet
{
    /// <summary>Empty set — caller should keep the legacy 1-token title gate.</summary>
    public static KeywordMatchPhraseSet Empty { get; } = new([], [], 0);

    /// <summary>Distinctive + primary garment (coverage chips).</summary>
    public List<KeywordMatchPhraseDto> Must { get; }

    /// <summary>Hardware / style / extra garments (score only).</summary>
    public List<KeywordMatchPhraseDto> Should { get; }

    /// <summary>Minimum distinctive hits for <see cref="KeywordMatchPhrases.PassesCoverage"/>.</summary>
    public int MinDistinctiveHits { get; }

    /// <summary>Create a phrase set.</summary>
    public KeywordMatchPhraseSet(
        List<KeywordMatchPhraseDto> must,
        List<KeywordMatchPhraseDto> should,
        int minDistinctiveHits = 0)
    {
        Must = must;
        Should = should;
        MinDistinctiveHits = minDistinctiveHits;
    }
}
