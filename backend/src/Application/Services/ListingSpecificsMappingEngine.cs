using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.DTOs.FourSeller;
using Application.DTOs.Listing;

namespace Application.Services;

/// <summary>
/// Pure parse + value-first matching helpers for description → 4Seller Specifics.
/// Kept free of I/O so unit tests can cover Shein/Temu <c>Key: Value</c> samples.
/// </summary>
public static class ListingSpecificsMappingEngine
{
    /// <summary>Minimum confidence accepted as a match candidate (surfaced in Matched for review).</summary>
    public const double MinAcceptConfidence = 0.75;

    /// <summary>
    /// Recommended confidence for default auto-select in the review modal.
    /// Weaker matches (≥ <see cref="MinAcceptConfidence"/>) still appear in Matched so the user can opt in.
    /// </summary>
    public const double MinAutoFillConfidence = 0.85;

    /// <summary>Minimum fuzzy score for a value hit across the whole schema.</summary>
    public const double MinValueFuzzyScore = 0.86;

    /// <summary>
    /// Softer threshold when mapping Care Instructions → 4Seller <c>Washing Instruction</c>
    /// (source text is often longer than the enum label, e.g. "Machine wash, do not dry clean").
    /// </summary>
    public const double MinWashingMapConfidence = 0.80;

    /// <summary>
    /// Care-mode tokens that justify a fuzzy wash boost — bare <c>wash</c> alone is too weak.
    /// </summary>
    private static readonly HashSet<string> DistinctiveWashModeTokens = new(StringComparer.Ordinal)
    {
        "hand", "machine", "dry", "clean", "bleach", "iron", "tumble"
    };

    /// <summary>
    /// Washing values that actually tell the buyer how to wash the item, best first.
    /// <para>
    /// Negative labels (<c>Do not dry clean</c>, <c>Do not wash</c>) state only what not to do, and
    /// <c>Dry clean</c> is wrong for everyday apparel, so none of them may be picked as a default —
    /// they are still used when the supplier description actually says so.
    /// </para>
    /// </summary>
    private static readonly string[] WashingMethodValueKeys =
    [
        "handwashonly",
        "handwash",
        "coldwashonly",
        "coldwash",
        "delicatewash",
        "machinewashable",
        "machinewash"
    ];

    /// <summary>Normalized forms of the negations that appear in supplier care text and TikTok enums.</summary>
    private static readonly string[] CareNegationPrefixes = ["donot", "dont", "never", "no"];

    /// <summary>Cap multi-select values per attribute to avoid dumping huge enum lists.</summary>
    public const int MaxMultiValuesPerAttribute = 5;

    /// <summary>TikTok Yes value id used by Prop65 / Dangerous goods enums.</summary>
    public const string TikTokYesValueId = "1000058";

    /// <summary>TikTok No value id used by Prop65 / Dangerous goods enums.</summary>
    public const string TikTokNoValueId = "1000059";

    private static readonly Regex TagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Captures concatenated <c>Key: Value Key: Value</c> blocks commonly found in Shein/Temu HTML
    /// (often a single line without newlines).
    /// Negative lookbehind <c>(?&lt;!&amp;\s)</c> keeps values like <c>Stage &amp; Concert</c> intact
    /// (otherwise <c>Concert …:</c> is mistaken for the next key).
    /// </summary>
    private static readonly Regex InlinePairRegex = new(
        @"(?<key>[A-Za-z][A-Za-z0-9 &/'\-]{0,78}?)\s*:\s*(?<value>.*?)(?=(?:\s+(?<!&\s)[A-Z][A-Za-z0-9 &/'\-]{0,78}?\s*:)|$)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Keys that never map into product Specifics (Brand + certs handled elsewhere).</summary>
    private static readonly HashSet<string> SkipSourceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "brand",
        "dangerous goods or hazardous materials",
        "ca prop 65: repro. chems",
        "ca prop 65: carcinogens",
        "hazardous",
        "prop 65"
    };

    /// <summary>
    /// Generic "head-noun" tokens that do not, on their own, indicate a meaningful match.
    /// </summary>
    private static readonly HashSet<string> GenericTokens = new(StringComparer.Ordinal)
    {
        "type", "size", "length", "width", "height", "material", "style", "color", "colour",
    };

    /// <summary>One schema value hit for a source description value.</summary>
    public sealed class ValueHit
    {
        /// <summary>Matched attribute.</summary>
        public required FourSellerCategoryAttributeDto Attr { get; init; }

        /// <summary>Matched enum value.</summary>
        public required FourSellerCategoryAttributeValueDto Value { get; init; }

        /// <summary>0–1 score.</summary>
        public double Score { get; init; }

        /// <summary><c>Exact</c> or <c>Fuzzy</c>.</summary>
        public required string Method { get; init; }
    }

    /// <summary>Source pair whose value appears under multiple category attributes.</summary>
    public sealed class ValueCollision
    {
        /// <summary>Original key from description.</summary>
        public required string SourceName { get; init; }

        /// <summary>Original value from description.</summary>
        public required string SourceValue { get; init; }

        /// <summary>Candidate attribute/value hits (same or near-same value name).</summary>
        public required List<ValueHit> Candidates { get; init; }
    }

    /// <summary>Parses <c>Key: Value</c> pairs from HTML or plain description text.</summary>
    public static List<(string Name, string Value)> ParseDescriptionPairs(string? description)
    {
        var text = ToPlainMultiline(description);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var pairs = new List<(string Name, string Value)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string name, string value)
        {
            name = name.Trim().TrimStart('•', '·', '-', '*', '–', '—').Trim();
            value = value.Trim();
            if (name.Length == 0 || value.Length == 0 || name.Length > 80)
            {
                return;
            }

            var dedupe = NormalizeKey(name) + "\0" + NormalizeKey(value);
            if (!seen.Add(dedupe))
            {
                return;
            }

            pairs.Add((name, value));
        }

        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Prefer inline multi-pair extraction when a line contains several "Key:" markers.
            var colonCount = 0;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == ':')
                {
                    colonCount++;
                }
            }

            if (colonCount >= 2)
            {
                var foundInline = false;
                foreach (Match m in InlinePairRegex.Matches(line))
                {
                    var key = m.Groups["key"].Value.Trim();
                    var value = m.Groups["value"].Value.Trim();
                    if (key.Length == 0 || value.Length == 0)
                    {
                        continue;
                    }

                    TryAdd(key, value);
                    foundInline = true;
                }

                if (foundInline)
                {
                    continue;
                }
            }

            var colon = line.IndexOf(':');
            if (colon <= 0 || colon >= line.Length - 1)
            {
                continue;
            }

            TryAdd(line[..colon], line[(colon + 1)..]);
        }

        // Also try whole-text inline parse when newlines were scarce but text is dense.
        if (pairs.Count < 2)
        {
            var collapsed = MultiSpaceRegex.Replace(text.Replace('\n', ' '), " ").Trim();
            foreach (Match m in InlinePairRegex.Matches(collapsed))
            {
                TryAdd(m.Groups["key"].Value, m.Groups["value"].Value);
            }
        }

        return pairs;
    }

    /// <summary>
    /// Filters category schema for Specifics map: drop certs, sale-props (Color/Size/Number of items),
    /// and attributes whose <c>requirementConditions</c> are not satisfied
    /// (default: Prop65/Dangerous goods = No → Carcinogen / Reprotoxic excluded).
    /// </summary>
    public static List<FourSellerCategoryAttributeDto> FilterSchemaForSpecificsMapping(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, string>? satisfiedValueIdByAttrId = null,
        TikTokAttributeValueDenylist? denylist = null)
    {
        var satisfied = satisfiedValueIdByAttrId
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return schema
            .Where(a => !IsCertificationAttribute(a.AttributeName))
            .Where(a => !IsSalePropAttribute(a))
            .Where(a => RequirementConditionsSatisfied(a, satisfied))
            .Select(a => WithoutRejectedValues(a, denylist))
            .ToList();
    }

    /// <summary>
    /// Same attribute without the values TikTok has rejected, so no downstream step can pick one —
    /// value-first matching, the AI prompt (fewer tokens too) and the default seed all read this list.
    /// Returns the original instance when nothing is filtered.
    /// <para>
    /// A mandatory attribute keeps its full enum when the denylist would empty it: on category
    /// <c>903824</c> five of the six <c>Washing instructions</c> values had been denied, and an empty
    /// enum turns "maybe this value" into 【Publish Failed】Specifics.Washing instructions every time.
    /// </para>
    /// </summary>
    public static FourSellerCategoryAttributeDto WithoutRejectedValues(
        FourSellerCategoryAttributeDto attr,
        TikTokAttributeValueDenylist? denylist)
    {
        if (denylist is null || denylist.IsEmpty || attr.Values.Count == 0)
        {
            return attr;
        }

        var kept = attr.Values
            .Where(v => !denylist.IsRejected(attr.AttributeId, v.ValueName))
            .ToList();
        if (kept.Count == attr.Values.Count || (attr.IsRequired && !kept.Any(v => AnswersRequired(attr, v))))
        {
            return attr;
        }

        return new FourSellerCategoryAttributeDto
        {
            AttributeId = attr.AttributeId,
            AttributeName = attr.AttributeName,
            IsRequired = attr.IsRequired,
            IsMultiple = attr.IsMultiple,
            IsCustomizable = attr.IsCustomizable,
            AttributeType = attr.AttributeType,
            RequirementConditionsJson = attr.RequirementConditionsJson,
            Values = kept
        };
    }

    /// <summary>
    /// True when the value can stand as the answer to a mandatory attribute.
    /// <para>
    /// Care attributes are the exception: 4Seller offers the "Do not …" values, TikTok does not accept
    /// them (<c>Do not wash</c> came back as a custom value name on 903824), so a denylist residue made
    /// only of prohibitions is as empty as no residue at all.
    /// </para>
    /// </summary>
    public static bool ValueCanAnswerRequiredAttribute(string? attributeName, string? valueName) =>
        !(IsWashingInstructionAttribute(attributeName) || IsCareInstructionsAttribute(attributeName))
        || !IsNegativeCareValueName(valueName);

    private static bool AnswersRequired(
        FourSellerCategoryAttributeDto attr,
        FourSellerCategoryAttributeValueDto value) =>
        ValueCanAnswerRequiredAttribute(attr.AttributeName, value.ValueName);

    /// <summary>
    /// Sale props belong on variations / <c>option{N}</c>, not <c>productAttributes</c>.
    /// </summary>
    public static bool IsSalePropAttribute(FourSellerCategoryAttributeDto attr)
    {
        var type = (attr.AttributeType ?? string.Empty).Trim();
        if (type is "2" or "sale" or "sales" or "sales_prop" or "salesProp")
        {
            return true;
        }

        var n = NormalizeKey(attr.AttributeName);
        return n is "color" or "colour" or "size" or "numberofitems" or "numberofitem";
    }

    /// <summary>
    /// True when every <c>VALUE_ID_MATCH</c> condition is met by <paramref name="satisfiedValueIdByAttrId"/>.
    /// Empty / unparsable conditions → unconditional (allowed).
    /// </summary>
    public static bool RequirementConditionsSatisfied(
        FourSellerCategoryAttributeDto attr,
        IReadOnlyDictionary<string, string> satisfiedValueIdByAttrId)
    {
        var raw = attr.RequirementConditionsJson;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw.Trim());
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return true;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var conditionType = ReadJsonString(item, "condition_type", "conditionType") ?? string.Empty;
                if (!string.Equals(conditionType, "VALUE_ID_MATCH", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parentAttrId = ReadJsonString(item, "attribute_id", "attributeId") ?? string.Empty;
                var requiredValueId = ReadJsonString(item, "attribute_value_id", "attributeValueId", "valueId")
                                      ?? string.Empty;
                if (string.IsNullOrWhiteSpace(parentAttrId) || string.IsNullOrWhiteSpace(requiredValueId))
                {
                    continue;
                }

                if (!satisfiedValueIdByAttrId.TryGetValue(parentAttrId.Trim(), out var actual)
                    || !string.Equals(actual?.Trim(), requiredValueId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>True when id parses as Int64 (TikTok attribute/value ids).</summary>
    public static bool IsInt64Id(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && long.TryParse(id.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// Value-first matching: look up the source value in the full category <c>valueList</c>,
    /// accept unique hits, and surface multi-attribute collisions for AI resolution.
    /// When <c>isMultipleSelected=1</c> and the source lists several values, collects multiple
    /// canonical enum entries under one matched row.
    /// </summary>
    public static (
        List<MappedListingSpecificDto> Matched,
        List<ValueCollision> Collisions,
        List<(string Name, string Value)> Remaining) MatchValueFirst(
        IReadOnlyList<(string Name, string Value)> pairs,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId)
    {
        var matched = new List<MappedListingSpecificDto>();
        var collisions = new List<ValueCollision>();
        var remaining = new List<(string Name, string Value)>();
        var usedAttrIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var index = BuildValueIndex(schema);

        foreach (var (name, value) in pairs)
        {
            if (ShouldSkipSourceKey(name))
            {
                remaining.Add((name, value));
                continue;
            }

            var hits = FindValueHits(value, index, usedAttrIds);
            if (hits.Count == 0)
            {
                if (TryMatchMultiValue(name, value, index, usedAttrIds, existingByAttrId, out var multi))
                {
                    matched.Add(multi);
                    usedAttrIds.Add(multi.AttrId);
                    continue;
                }

                remaining.Add((name, value));
                continue;
            }

            // Distinct attributes among hits.
            var byAttr = hits
                .GroupBy(h => h.Attr.AttributeId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(h => h.Score).First())
                .OrderByDescending(h => h.Score)
                .ToList();

            if (byAttr.Count > 1)
            {
                // Care/Washing source → prefer the schema Washing Instruction attr when present.
                if (IsCareInstructionSourceKey(name))
                {
                    var washingHit = byAttr.FirstOrDefault(h => IsWashingInstructionAttribute(h.Attr.AttributeName));
                    if (washingHit is not null)
                    {
                        byAttr = [washingHit];
                    }
                }
            }

            if (byAttr.Count > 1)
            {
                // Ambiguous — AI must choose among candidates.
                collisions.Add(new ValueCollision
                {
                    SourceName = name,
                    SourceValue = value,
                    Candidates = byAttr
                });
                continue;
            }

            var hit = byAttr[0];
            if (hit.Score < MinAcceptConfidence)
            {
                if (TryMatchMultiValue(name, value, index, usedAttrIds, existingByAttrId, out var multiWeak))
                {
                    matched.Add(multiWeak);
                    usedAttrIds.Add(multiWeak.AttrId);
                    continue;
                }

                remaining.Add((name, value));
                continue;
            }

            // Unique full-string hit — still try multi when attr allows and source has separators.
            if (hit.Attr.IsMultiple
                && TryMatchMultiValue(name, value, index, usedAttrIds, existingByAttrId, out var multiPreferred)
                && multiPreferred.Values.Count > 1
                && string.Equals(multiPreferred.AttrId, hit.Attr.AttributeId, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(multiPreferred);
                usedAttrIds.Add(multiPreferred.AttrId);
                continue;
            }

            usedAttrIds.Add(hit.Attr.AttributeId);
            matched.Add(CreateMapped(
                name,
                value,
                hit.Attr,
                [hit.Value],
                hit.Score,
                hit.Method == "Exact" ? "ValueExact" : "ValueFuzzy",
                existingByAttrId));
        }

        return (matched, collisions, remaining);
    }

    /// <summary>
    /// Splits a source value into tokens and, when they all hit the same <c>isMultiple</c> attr,
    /// returns one multi-value mapped row.
    /// </summary>
    private static bool TryMatchMultiValue(
        string sourceName,
        string sourceValue,
        List<(FourSellerCategoryAttributeDto Attr, FourSellerCategoryAttributeValueDto Value, string Norm)> index,
        ISet<string> usedAttrIds,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        out MappedListingSpecificDto mapped)
    {
        mapped = null!;
        var tokens = SplitMultiValueTokens(sourceValue);
        if (tokens.Count < 2)
        {
            return false;
        }

        // attrId → best ValueHit per valueId
        var byAttr = new Dictionary<string, List<ValueHit>>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var hits = FindValueHits(token, index, usedAttrIds);
            foreach (var hit in hits.Where(h => h.Attr.IsMultiple && h.Score >= MinAcceptConfidence))
            {
                if (!byAttr.TryGetValue(hit.Attr.AttributeId, out var list))
                {
                    list = [];
                    byAttr[hit.Attr.AttributeId] = list;
                }

                if (list.Any(h =>
                        string.Equals(h.Value.ValueId, hit.Value.ValueId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                list.Add(hit);
            }
        }

        var best = byAttr
            .Select(kv => (
                AttrId: kv.Key,
                Hits: kv.Value.OrderByDescending(h => h.Score).Take(MaxMultiValuesPerAttribute).ToList()))
            .Where(x => x.Hits.Count >= 2)
            .OrderByDescending(x => x.Hits.Count)
            .ThenByDescending(x => x.Hits.Average(h => h.Score))
            .FirstOrDefault();

        if (best.Hits is null || best.Hits.Count < 2)
        {
            return false;
        }

        var attr = best.Hits[0].Attr;
        mapped = CreateMapped(
            sourceName,
            sourceValue,
            attr,
            best.Hits.Select(h => h.Value).ToList(),
            best.Hits.Average(h => h.Score),
            "ValueMulti",
            existingByAttrId);
        return true;
    }

    /// <summary>Splits comma / slash / ampersand / "and" lists into candidate value tokens.</summary>
    public static List<string> SplitMultiValueTokens(string? sourceValue)
    {
        if (string.IsNullOrWhiteSpace(sourceValue))
        {
            return [];
        }

        var raw = sourceValue.Trim();
        var parts = Regex.Split(raw, @"\s*(?:,|/|\||&|\band\b)\s*", RegexOptions.IgnoreCase)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts.Count >= 2 ? parts : [raw];
    }

    private static MappedListingSpecificDto CreateMapped(
        string sourceName,
        string sourceValue,
        FourSellerCategoryAttributeDto attr,
        IReadOnlyList<FourSellerCategoryAttributeValueDto> values,
        double confidence,
        string matchMethod,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId)
    {
        var limited = values
            .Take(attr.IsMultiple ? MaxMultiValuesPerAttribute : 1)
            .ToList();
        if (limited.Count == 0)
        {
            limited = values.Take(1).ToList();
        }

        var mappedValues = limited
            .Select(v => new MappedListingSpecificValueDto
            {
                ValueId = string.IsNullOrWhiteSpace(v.ValueId) ? null : v.ValueId,
                ValueName = v.ValueName
            })
            .ToList();

        var skipped = existingByAttrId.TryGetValue(attr.AttributeId, out var existing)
                      && !string.IsNullOrWhiteSpace(existing.AttrValue);

        return new MappedListingSpecificDto
        {
            SourceName = sourceName,
            SourceValue = sourceValue,
            AttrId = attr.AttributeId,
            AttrName = attr.AttributeName,
            AttrValue = string.Join(", ", mappedValues.Select(v => v.ValueName)),
            ValueId = mappedValues.FirstOrDefault()?.ValueId,
            Values = mappedValues,
            IsRequired = attr.IsRequired || IsWashingInstructionAttribute(attr.AttributeName),
            Confidence = Math.Round(Math.Clamp(confidence, 0, 1), 3),
            MatchMethod = matchMethod,
            SkippedExisting = skipped
        };
    }

    /// <summary>
    /// Legacy wrapper — value-first match, collisions folded into remaining for callers
    /// that do not handle collisions yet.
    /// </summary>
    public static (
        List<MappedListingSpecificDto> Matched,
        List<(string Name, string Value)> Remaining) MatchDeterministic(
        IReadOnlyList<(string Name, string Value)> pairs,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId)
    {
        var (matched, collisions, remaining) = MatchValueFirst(pairs, schema, existingByAttrId);
        foreach (var c in collisions)
        {
            remaining.Add((c.SourceName, c.SourceValue));
        }

        return (matched, remaining);
    }

    /// <summary>
    /// Validates AI JSON suggestions against the schema. Rejects invented ids.
    /// Optional <paramref name="allowedAttrIdsBySource"/> restricts each source pair to a candidate set
    /// (used for value-collision resolution).
    /// Supports multi-select via <c>values:[{valueId,valueName}]</c> when schema <c>isMultiple</c>.
    /// </summary>
    public static List<MappedListingSpecificDto> ValidateAiSuggestions(
        string? aiJson,
        IReadOnlyList<(string Name, string Value)> remainingPairs,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        ISet<string> alreadyMatchedAttrIds,
        string matchMethod = "Ai",
        IReadOnlyDictionary<string, IReadOnlySet<string>>? allowedAttrIdsBySource = null)
    {
        var result = new List<MappedListingSpecificDto>();
        if (string.IsNullOrWhiteSpace(aiJson))
        {
            return result;
        }

        var schemaById = schema.ToDictionary(a => a.AttributeId, StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonArray(aiJson));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var sourceName = ReadJsonString(item, "sourceName", "source_name", "name") ?? string.Empty;
                var sourceValue = ReadJsonString(item, "sourceValue", "source_value", "value") ?? string.Empty;
                var attrId = ReadJsonString(item, "attributeId", "attrId", "attribute_id") ?? string.Empty;
                var confidence = ReadJsonDouble(item, "confidence") ?? 0.7;

                if (string.IsNullOrWhiteSpace(attrId)
                    || !IsInt64Id(attrId)
                    || !schemaById.TryGetValue(attrId.Trim(), out var attr))
                {
                    continue;
                }

                if (alreadyMatchedAttrIds.Contains(attr.AttributeId))
                {
                    continue;
                }

                if (confidence < MinAcceptConfidence)
                {
                    continue;
                }

                if (allowedAttrIdsBySource is not null
                    && !string.IsNullOrWhiteSpace(sourceName)
                    && allowedAttrIdsBySource.TryGetValue(NormalizeKey(sourceName), out var allowed)
                    && !allowed.Contains(attr.AttributeId))
                {
                    continue;
                }

                var resolvedValues = ResolveAiValues(item, attr, sourceValue);
                if (resolvedValues.Count == 0)
                {
                    continue;
                }

                if (!attr.IsMultiple && resolvedValues.Count > 1)
                {
                    resolvedValues = [resolvedValues[0]];
                }

                if (string.IsNullOrWhiteSpace(sourceName) && remainingPairs.Count > 0)
                {
                    sourceName = remainingPairs[0].Name ?? attr.AttributeName;
                    sourceValue = remainingPairs[0].Value ?? resolvedValues[0].ValueName;
                }

                alreadyMatchedAttrIds.Add(attr.AttributeId);
                result.Add(CreateMapped(
                    string.IsNullOrWhiteSpace(sourceName) ? attr.AttributeName : sourceName.Trim(),
                    string.IsNullOrWhiteSpace(sourceValue) ? resolvedValues[0].ValueName : sourceValue.Trim(),
                    attr,
                    resolvedValues,
                    confidence,
                    matchMethod,
                    existingByAttrId));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return result;
    }

    /// <summary>
    /// Resolves AI-proposed value(s) onto schema enums. Prefer <c>values[]</c>; fall back to single
    /// valueId/valueName. Invented ids/names are dropped.
    /// </summary>
    private static List<FourSellerCategoryAttributeValueDto> ResolveAiValues(
        JsonElement item,
        FourSellerCategoryAttributeDto attr,
        string sourceValue)
    {
        var resolved = new List<FourSellerCategoryAttributeValueDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? valueId, string? valueName)
        {
            if (resolved.Count >= (attr.IsMultiple ? MaxMultiValuesPerAttribute : 1))
            {
                return;
            }

            FourSellerCategoryAttributeValueDto? match = null;
            if (!string.IsNullOrWhiteSpace(valueId) && IsInt64Id(valueId))
            {
                match = attr.Values.FirstOrDefault(v =>
                    string.Equals(v.ValueId, valueId.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (match is null && !string.IsNullOrWhiteSpace(valueName))
            {
                match = attr.Values.FirstOrDefault(v =>
                    string.Equals(v.ValueName, valueName.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (match is not null)
            {
                var key = string.IsNullOrWhiteSpace(match.ValueId) ? match.ValueName : match.ValueId;
                if (seen.Add(key))
                {
                    resolved.Add(match);
                }

                return;
            }

            // Custom free-text only for an attribute that offers no enum at all. 4Seller's
            // isCustomizable cannot decide this: for category 903824 it marks Washing instructions
            // (100401) customizable, yet TikTok answers "attribute ID 100401 does not support custom
            // value names". Whenever the enum exists, an unmatched value is dropped instead.
            if (attr.Values.Count == 0 && string.IsNullOrWhiteSpace(valueId))
            {
                var custom = string.IsNullOrWhiteSpace(valueName) ? sourceValue.Trim() : valueName.Trim();
                if (string.IsNullOrWhiteSpace(custom))
                {
                    return;
                }

                if (seen.Add("\0" + custom))
                {
                    resolved.Add(new FourSellerCategoryAttributeValueDto
                    {
                        ValueId = string.Empty,
                        ValueName = custom
                    });
                }
            }
        }

        if (item.TryGetProperty("values", out var valuesEl) && valuesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in valuesEl.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                TryAdd(
                    ReadJsonString(v, "valueId", "value_id"),
                    ReadJsonString(v, "valueName", "value_name", "name"));
            }
        }

        if (resolved.Count == 0)
        {
            TryAdd(
                ReadJsonString(item, "valueId", "value_id"),
                ReadJsonString(item, "valueName", "value_name", "attrValue"));
        }

        return resolved;
    }

    /// <summary>Builds missing-required list excluding already filled attr ids.</summary>
    public static List<MissingRequiredSpecificDto> BuildMissingRequired(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IEnumerable<string> filledAttrIds,
        IEnumerable<string>? forceRequiredAttrIds = null)
    {
        var filled = new HashSet<string>(filledAttrIds, StringComparer.OrdinalIgnoreCase);
        var force = new HashSet<string>(
            forceRequiredAttrIds ?? [],
            StringComparer.OrdinalIgnoreCase);

        // RequiredSpecificAttributes drops what can never sit in productAttributes (certs, sale props)
        // and what the category does not ask for yet (a closed requirementConditions gate), so a gated
        // chemical list is not reported as a hole the seller has to fill.
        return RequiredSpecificAttributes(schema)
            .Concat(schema.Where(a => !a.IsRequired && force.Contains(a.AttributeId)))
            .Where(a => !filled.Contains(a.AttributeId) && !IsCertificationAttribute(a.AttributeName))
            .GroupBy(a => a.AttributeId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MissingRequiredSpecificDto
            {
                AttrId = g.Key,
                AttrName = g.First().AttributeName
            })
            .ToList();
    }

    /// <summary>
    /// True when the schema attribute is TikTok/4Seller <c>Washing Instruction(s)</c>.
    /// Does <b>not</b> match Care Instructions — that is a description source synonym only.
    /// </summary>
    public static bool IsWashingInstructionAttribute(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var n = NormalizeKey(name);
        return n is "washinginstruction" or "washinginstructions"
            || n.Contains("washinginstruction", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when a description key is a care/washing synonym (Shein/Temu often use Care Instructions).
    /// </summary>
    public static bool IsCareInstructionSourceKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var n = NormalizeKey(name);
        return n is "washinginstruction" or "washinginstructions"
            or "careinstruction" or "careinstructions"
            or "laundryinstruction" or "laundryinstructions"
            or "washinstruction" or "washinstructions"
            || n.Contains("washinginstruction", StringComparison.Ordinal)
            || n.Contains("careinstruction", StringComparison.Ordinal)
            || n.Contains("laundryinstruction", StringComparison.Ordinal);
    }

    /// <summary>
    /// Fashion/apparel signal: category path/name keywords, or schema already exposes Washing Instruction.
    /// </summary>
    public static bool IsFashionCategory(
        string? categoryName,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema)
    {
        if (schema.Any(a => IsWashingInstructionAttribute(a.AttributeName)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return false;
        }

        var n = categoryName.Trim().ToLowerInvariant();
        return n.Contains("apparel", StringComparison.Ordinal)
               || n.Contains("clothing", StringComparison.Ordinal)
               || n.Contains("fashion", StringComparison.Ordinal)
               || n.Contains("dress", StringComparison.Ordinal)
               || n.Contains("shirt", StringComparison.Ordinal)
               || n.Contains("pants", StringComparison.Ordinal)
               || n.Contains("trouser", StringComparison.Ordinal)
               || n.Contains("skirt", StringComparison.Ordinal)
               || n.Contains("blouse", StringComparison.Ordinal)
               || n.Contains("jacket", StringComparison.Ordinal)
               || n.Contains("coat", StringComparison.Ordinal)
               || n.Contains("sweater", StringComparison.Ordinal)
               || n.Contains("hoodie", StringComparison.Ordinal)
               || n.Contains("lingerie", StringComparison.Ordinal)
               || n.Contains("underwear", StringComparison.Ordinal)
               || n.Contains("swimwear", StringComparison.Ordinal)
               || n.Contains("bikini", StringComparison.Ordinal)
               || n.Contains("activewear", StringComparison.Ordinal)
               || n.Contains("socks", StringComparison.Ordinal)
               || n.Contains("footwear", StringComparison.Ordinal)
               || n.Contains("shoes", StringComparison.Ordinal)
               || n.Contains("women", StringComparison.Ordinal)
               || n.Contains("men", StringComparison.Ordinal)
               || n.Contains("kids", StringComparison.Ordinal);
    }

    /// <summary>Finds the 4Seller Washing Instruction attribute on the category schema.</summary>
    public static FourSellerCategoryAttributeDto? FindWashingInstructionAttribute(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema) =>
        schema.FirstOrDefault(a => IsWashingInstructionAttribute(a.AttributeName));

    /// <summary>
    /// For fashion categories: map Care Instructions → Washing Instruction (4Seller),
    /// force <c>IsRequired</c>, and surface it in missing-required when still empty.
    /// </summary>
    public static void EnsureFashionWashingInstruction(
        List<MappedListingSpecificDto> matched,
        List<UnmappedListingSpecificDto> unmatched,
        IReadOnlyList<(string Name, string Value)> pairs,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        string? categoryName,
        TikTokAttributeValueDenylist? denylist = null)
    {
        if (!IsFashionCategory(categoryName, schema))
        {
            return;
        }

        var washing = FindWashingInstructionAttribute(schema);
        if (washing is null)
        {
            return;
        }

        // Already matched → force required for fashion.
        var existingMatch = matched.FirstOrDefault(m =>
            string.Equals(m.AttrId, washing.AttributeId, StringComparison.OrdinalIgnoreCase)
            || IsWashingInstructionAttribute(m.AttrName));
        if (existingMatch is not null)
        {
            existingMatch.IsRequired = true;
            return;
        }

        // Already filled on the form.
        if (existingByAttrId.TryGetValue(washing.AttributeId, out var existingRow)
            && !string.IsNullOrWhiteSpace(existingRow.AttrValue))
        {
            return;
        }

        // Prefer Care Instructions / Washing synonyms from description.
        var carePairs = pairs
            .Where(p => IsCareInstructionSourceKey(p.Name))
            .ToList();
        if (carePairs.Count == 0)
        {
            // Fallback: any unmatched row whose value looks like a wash instruction.
            carePairs = pairs
                .Where(p => LooksLikeWashCareValue(p.Value))
                .Take(1)
                .ToList();
        }

        foreach (var (sourceName, sourceValue) in carePairs)
        {
            var mapped = TryMapOntoWashingInstruction(
                sourceName,
                sourceValue,
                washing,
                existingByAttrId,
                denylist);
            if (mapped is null)
            {
                continue;
            }

            matched.Add(mapped);

            // Drop corresponding unmatched care rows if present.
            unmatched.RemoveAll(u =>
                string.Equals(
                    NormalizeKey(u.SourceName),
                    NormalizeKey(sourceName),
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    NormalizeKey(u.SourceValue),
                    NormalizeKey(sourceValue),
                    StringComparison.OrdinalIgnoreCase));

            return;
        }

        // Fashion requires Washing Instruction — seed a TikTok enum (never free-text custom).
        var defaultValue = ResolveDefaultWashingInstructionValue(washing, denylist);
        if (defaultValue is null)
        {
            return;
        }

        var seeded = CreateMapped(
            washing.AttributeName,
            defaultValue.ValueName,
            washing,
            [defaultValue],
            1.0,
            "WashingDefault",
            existingByAttrId);
        seeded.IsRequired = true;
        matched.Add(seeded);
    }

    /// <summary>True when the schema attribute is TikTok/4Seller <c>Care instructions</c> (101303).</summary>
    public static bool IsCareInstructionsAttribute(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return NormalizeKey(name).Contains("careinstruction", StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps <c>Washing instructions</c> (100401) and <c>Care instructions</c> (101303) telling the same
    /// story. Both describe how to wash the item and their enums overlap, so filling them independently
    /// can ship a page that says Hand wash in one field and Dry clean in the other — exactly what TikTok
    /// flags as inconsistent product information.
    /// <para>
    /// Only a seeded washing default is realigned: a value that came from the supplier description is
    /// evidence and stays untouched.
    /// </para>
    /// </summary>
    public static void AlignWashingWithCareInstructions(
        List<MappedListingSpecificDto> matched,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        TikTokAttributeValueDenylist? denylist = null)
    {
        var washing = FindWashingInstructionAttribute(schema);
        if (washing is null || washing.Values.Count == 0)
        {
            return;
        }

        var seededWashing = matched.FirstOrDefault(m =>
            string.Equals(m.AttrId, washing.AttributeId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.MatchMethod, "WashingDefault", StringComparison.Ordinal));
        if (seededWashing is null)
        {
            return;
        }

        var careAttr = schema.FirstOrDefault(a => IsCareInstructionsAttribute(a.AttributeName));
        if (careAttr is null)
        {
            return;
        }

        var careValue = matched
                            .FirstOrDefault(m => string.Equals(
                                m.AttrId,
                                careAttr.AttributeId,
                                StringComparison.OrdinalIgnoreCase))
                            ?.AttrValue
                        ?? (existingByAttrId.TryGetValue(careAttr.AttributeId, out var careRow)
                            ? careRow.AttrValue
                            : null);
        if (string.IsNullOrWhiteSpace(careValue) || IsNegativeCareValueName(careValue))
        {
            return;
        }

        var aligned = ResolveBestWashingEnumValue(
            careValue,
            washing.Values,
            washing.AttributeId,
            denylist);
        if (aligned is null
            || string.Equals(
                NormalizeKey(aligned.ValueName),
                NormalizeKey(seededWashing.AttrValue),
                StringComparison.Ordinal))
        {
            return;
        }

        seededWashing.AttrValue = aligned.ValueName;
        seededWashing.ValueId = aligned.ValueId;
        seededWashing.Values =
        [
            new MappedListingSpecificValueDto
            {
                ValueId = aligned.ValueId,
                ValueName = aligned.ValueName
            }
        ];
        seededWashing.SourceName = careAttr.AttributeName;
        seededWashing.SourceValue = careValue.Trim();
        seededWashing.MatchMethod = "WashingCareAligned";
    }

    /// <summary>
    /// True when the label only forbids something (<c>Do not dry clean</c>, <c>Do not wash</c>)
    /// instead of describing how to wash the item.
    /// </summary>
    public static bool IsNegativeCareValueName(string? valueName)
    {
        var n = NormalizeKey(valueName);
        return n.StartsWith("donot", StringComparison.Ordinal)
               || n.StartsWith("dont", StringComparison.Ordinal)
               || n.StartsWith("never", StringComparison.Ordinal);
    }

    /// <summary>
    /// Default washing value when the supplier description says nothing about care: the enum that
    /// describes an actual washing method, preferring the gentlest one the category offers.
    /// <para>
    /// When every preferred method is denied it widens step by step — first a denied washing method, only
    /// then a prohibition (<c>Do not wash</c>) — instead of returning null. Washing instructions is
    /// mandatory for apparel, and an empty mandatory attribute is not "no answer": 4Seller refuses the
    /// whole publish with <c>Specifics.Washing instructions</c>. That is exactly what happened on
    /// category <c>903824</c>, where five of the six values had reached the denylist and the attribute
    /// was left empty on every retry.
    /// </para>
    /// <para>
    /// The one filter that never widens is agreement with the supplier's care text: mapping
    /// "do not dry clean" onto <c>Dry clean</c> publishes care advice the description contradicts.
    /// Returns null when nothing in the enum agrees with it, which the caller reports through
    /// <c>MissingRequired</c>.
    /// </para>
    /// </summary>
    public static FourSellerCategoryAttributeValueDto? ResolveDefaultWashingInstructionValue(
        FourSellerCategoryAttributeDto washingAttr,
        TikTokAttributeValueDenylist? denylist = null,
        string? sourceCareText = null)
    {
        var sourceNorm = NormalizeKey(sourceCareText);
        var named = washingAttr.Values
            .Where(v => !string.IsNullOrWhiteSpace(v.ValueName))
            .ToList();

        bool NotDenied(FourSellerCategoryAttributeValueDto v) =>
            denylist?.IsRejected(washingAttr.AttributeId, v.ValueName) != true;

        // A value the supplier explicitly forbids ("do not dry clean" → Dry clean) is worse than
        // an empty attribute: it ships care advice that contradicts the description, which TikTok
        // rejects on its own as PDP Inconsistent Information.
        bool AgreesWithSource(FourSellerCategoryAttributeValueDto v) =>
            sourceNorm.Length == 0 || !ForbidsTreatment(sourceNorm, NormalizeKey(v.ValueName));

        bool Positive(FourSellerCategoryAttributeValueDto v) => !IsNegativeCareValueName(v.ValueName);

        // Widening tiers, best first. The attribute is mandatory for apparel, so a denied value and even
        // a prohibition beat an empty row: 4Seller answers an empty mandatory row with
        // 【Publish Failed】Specifics.Washing instructions, which fails every retry the same way.
        //
        // A denied washing method outranks a prohibition on purpose. 4Seller lists the "Do not …" values
        // for 903824, TikTok does not accept them (`Do not wash` came back as a custom value name), while
        // `Hand wash only` — denied from earlier failures that were really missing valueIds — published
        // fine once the live schema supplied its id.
        //
        // What never widens is the agreement with the description: shipping "Dry clean" for an item
        // whose care text says "do not dry clean" is wrong data, not a lesser evil.
        Func<FourSellerCategoryAttributeValueDto, bool>[] tiers =
        [
            v => Positive(v) && NotDenied(v) && AgreesWithSource(v),
            v => Positive(v) && AgreesWithSource(v),
            v => NotDenied(v) && AgreesWithSource(v),
            AgreesWithSource
        ];

        var candidates = new List<FourSellerCategoryAttributeValueDto>();
        foreach (var tier in tiers)
        {
            candidates = named.Where(tier).ToList();
            if (candidates.Count > 0)
            {
                break;
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        foreach (var key in WashingMethodValueKeys)
        {
            var exact = candidates.FirstOrDefault(v => NormalizeKey(v.ValueName) == key);
            if (exact is not null)
            {
                return exact;
            }
        }

        foreach (var key in WashingMethodValueKeys)
        {
            var partial = candidates.FirstOrDefault(v =>
                NormalizeKey(v.ValueName).Contains(key, StringComparison.Ordinal));
            if (partial is not null)
            {
                return partial;
            }
        }

        // Last resort: a value the category still offers beats an empty mandatory attribute.
        return candidates[0];
    }

    /// <summary>True when the schema attribute is TikTok/4Seller <c>Condition type</c>.</summary>
    public static bool IsConditionTypeAttribute(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var n = NormalizeKey(name);
        return n is "conditiontype" or "condition";
    }

    /// <summary>True when the schema attribute is TikTok/4Seller <c>Condition description</c>.</summary>
    public static bool IsConditionDescriptionAttribute(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var n = NormalizeKey(name);
        return n is "conditiondescription" || n.Contains("conditiondescription", StringComparison.Ordinal);
    }

    /// <summary>Finds the 4Seller Condition type attribute on the category schema.</summary>
    public static FourSellerCategoryAttributeDto? FindConditionTypeAttribute(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema) =>
        schema.FirstOrDefault(a => IsConditionTypeAttribute(a.AttributeName));

    /// <summary>Finds the 4Seller Condition description attribute on the category schema.</summary>
    public static FourSellerCategoryAttributeDto? FindConditionDescriptionAttribute(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema) =>
        schema.FirstOrDefault(a => IsConditionDescriptionAttribute(a.AttributeName));

    /// <summary>
    /// When the category schema requires Condition type/description, seed defaults for new apparel
    /// (Condition type = New, Condition description = Brand new) if still empty.
    /// </summary>
    public static void EnsureConditionSpecifics(
        List<MappedListingSpecificDto> matched,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId)
    {
        var typeAttr = FindConditionTypeAttribute(schema);
        var descAttr = FindConditionDescriptionAttribute(schema);
        if (typeAttr is null && descAttr is null)
        {
            return;
        }

        var usedAttrIds = new HashSet<string>(
            matched.Select(m => m.AttrId).Concat(existingByAttrId.Keys),
            StringComparer.OrdinalIgnoreCase);

        if (typeAttr is not null
            && !usedAttrIds.Contains(typeAttr.AttributeId)
            && !HasFilledConditionRow(existingByAttrId, typeAttr.AttributeId))
        {
            var newValue = ResolveDefaultConditionTypeValue(typeAttr);
            if (newValue is not null)
            {
                var mapped = CreateMapped(
                    typeAttr.AttributeName,
                    newValue.ValueName,
                    typeAttr,
                    [newValue],
                    1.0,
                    "ConditionDefault",
                    existingByAttrId);
                mapped.IsRequired = true;
                matched.Add(mapped);
                usedAttrIds.Add(typeAttr.AttributeId);
            }
        }

        if (descAttr is not null
            && !usedAttrIds.Contains(descAttr.AttributeId)
            && !HasFilledConditionRow(existingByAttrId, descAttr.AttributeId))
        {
            const string defaultDescription = "Brand new";
            var mapped = CreateMapped(
                descAttr.AttributeName,
                defaultDescription,
                descAttr,
                [
                    new FourSellerCategoryAttributeValueDto
                    {
                        ValueId = string.Empty,
                        ValueName = defaultDescription
                    }
                ],
                1.0,
                "ConditionDefault",
                existingByAttrId);
            mapped.IsRequired = true;
            matched.Add(mapped);
        }
    }

    /// <summary>
    /// When the category schema requires Region of origin / CPSIA tracking label, seed safe defaults
    /// (China / No) if still empty — TikTok returns 【Publish Failed】Specifics.* when these mandatory
    /// rows are missing.
    /// </summary>
    public static void EnsureRegionAndCpsiaSpecifics(
        List<MappedListingSpecificDto> matched,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId)
    {
        EnsureDefaultSpecific(
            matched,
            existingByAttrId,
            FindRegionOfOriginAttribute(schema),
            ResolveDefaultRegionOfOriginValue,
            "RegionOfOriginDefault");
        EnsureDefaultSpecific(
            matched,
            existingByAttrId,
            FindCpsiaTrackingLabelAttribute(schema),
            ResolveDefaultCpsiaTrackingLabelValue,
            "CpsiaDefault");
    }

    private static void EnsureDefaultSpecific(
        List<MappedListingSpecificDto> matched,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        FourSellerCategoryAttributeDto? attr,
        Func<FourSellerCategoryAttributeDto, FourSellerCategoryAttributeValueDto?> resolveDefault,
        string method)
    {
        if (attr is null)
        {
            return;
        }

        var usedAttrIds = new HashSet<string>(
            matched.Select(m => m.AttrId).Concat(existingByAttrId.Keys),
            StringComparer.OrdinalIgnoreCase);
        if (usedAttrIds.Contains(attr.AttributeId)
            || HasFilledConditionRow(existingByAttrId, attr.AttributeId))
        {
            return;
        }

        var value = resolveDefault(attr);
        if (value is null)
        {
            return;
        }

        var mapped = CreateMapped(
            attr.AttributeName,
            value.ValueName,
            attr,
            [value],
            1.0,
            method,
            existingByAttrId);
        mapped.IsRequired = attr.IsRequired;
        matched.Add(mapped);
    }

    /// <summary>
    /// The attributes a category really refuses to publish without: <c>isMandatory=1</c> minus the ones
    /// that never belong in <c>productAttributes</c> (certs, sale props) or whose
    /// <c>requirementConditions</c> gate is not open (the Prop 65 chemical lists).
    /// <para>
    /// The set is category-specific and can be empty (<c>897040</c>) or long (<c>903824</c>: Weaving
    /// method, Washing instructions, Region of origin, Material), which is why it is always read off the
    /// live schema instead of a hardcoded list.
    /// </para>
    /// </summary>
    public static List<FourSellerCategoryAttributeDto> RequiredSpecificAttributes(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, string>? satisfiedValueIdByAttrId = null)
    {
        var satisfied = satisfiedValueIdByAttrId ?? DefaultCertificationAnswers();
        return schema
            .Where(a => a.IsRequired)
            .Where(a => !IsCertificationAttribute(a.AttributeName))
            .Where(a => !IsSalePropAttribute(a))
            .Where(a => RequirementConditionsSatisfied(a, satisfied))
            .ToList();
    }

    /// <summary>
    /// The Yes/No answers we always send for certification gates, so gated attributes (Carcinogen,
    /// Reprotoxic chemicals) stay out of the payload and out of the mandatory set.
    /// </summary>
    public static Dictionary<string, string> DefaultCertificationAnswers() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["101619"] = TikTokNoValueId, // Dangerous goods or hazardous materials
            ["101395"] = TikTokNoValueId, // CA prop 65: repro. chems
            ["101400"] = TikTokNoValueId  // CA prop 65: carcinogens
        };

    /// <summary>
    /// Value to send for a mandatory attribute the supplier description says nothing about. Leaving it
    /// empty is not an option: 4Seller answers 【Publish Failed】Specifics.&lt;attribute name&gt;.
    /// <para>
    /// Well-known attributes get a deliberate answer (Region of origin → China, Weaving method → Not
    /// applicable, a Yes/No gate → No, Material → a fibre the product title already named when the
    /// category enum offers it, otherwise the most common apparel fibre the category offers);
    /// anything else falls back to the first enum value that is not a prohibition, since "Do not wash"
    /// as a default would ship care advice nobody meant. Returns null when the category exposes no
    /// value at all — a free-text mandatory attribute cannot be invented, it must be reported.
    /// </para>
    /// </summary>
    public static FourSellerCategoryAttributeValueDto? ResolveRequiredDefaultValue(
        FourSellerCategoryAttributeDto attr,
        TikTokAttributeValueDenylist? denylist = null,
        string? sourceCareText = null,
        string? sourceTitleText = null)
    {
        if (IsWashingInstructionAttribute(attr.AttributeName) || IsCareInstructionsAttribute(attr.AttributeName))
        {
            return ResolveDefaultWashingInstructionValue(attr, denylist, sourceCareText);
        }

        var named = attr.Values
            .Where(v => !string.IsNullOrWhiteSpace(v.ValueName))
            .ToList();
        var candidates = named
            .Where(v => denylist?.IsRejected(attr.AttributeId, v.ValueName) != true)
            .ToList();

        // The row is mandatory, so a denied value still beats an empty row — the denylist narrows the
        // choice, it cannot remove the obligation to send one.
        if (candidates.Count == 0)
        {
            candidates = named;
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (IsRegionOfOriginAttribute(attr.AttributeName) || IsCountryOfOriginAttribute(attr.AttributeName))
        {
            return PreferByName(candidates, ["china"]) ?? candidates[0];
        }

        if (IsCpsiaTrackingLabelAttribute(attr.AttributeName))
        {
            return PreferByName(candidates, ["no", "notapplicable"]) ?? candidates[0];
        }

        if (IsConditionTypeAttribute(attr.AttributeName))
        {
            return PreferByName(candidates, ["new"]) ?? candidates[0];
        }

        if (IsYesNoAttribute(candidates))
        {
            return PreferByName(candidates, ["no", "notapplicable", "notsure"]) ?? candidates[0];
        }

        if (IsWeavingMethodAttribute(attr.AttributeName))
        {
            return PreferByName(candidates, ["notapplicable", "woven", "knitting"]) ?? candidates[0];
        }

        if (IsMaterialAttribute(attr.AttributeName))
        {
            // Title-named fibres beat the generic Polyester fallback so the PDP attribute cannot
            // contradict a Linen/Cotton claim the supplier already put in the product name.
            return PreferByName(candidates, FabricKeysFromText(sourceTitleText))
                ?? PreferByName(candidates, CommonApparelMaterialKeys)
                ?? candidates[0];
        }

        // Nothing special about this attribute: any real option beats an empty mandatory row, but a
        // prohibition ("Do not …") states only what not to do and reads as a mistake on the PDP.
        return candidates.FirstOrDefault(v => !IsNegativeCareValueName(v.ValueName)) ?? candidates[0];
    }

    /// <summary>
    /// Seeds every mandatory attribute the category still has empty, so the Specifics step leaves no
    /// row for 4Seller to complain about.
    /// <para>
    /// A twin attribute is honoured first: <c>903824</c> carries two attributes named "Material"
    /// (<c>100157</c> mandatory, <c>100701</c> optional), and matching by name can easily fill only the
    /// optional one — copying the value across keeps both consistent instead of seeding a default that
    /// contradicts it.
    /// </para>
    /// <para>
    /// <paramref name="sourceTitleText"/> is used only when Material has no mapped value — picks an
    /// enum fibre the title already named instead of inventing Polyester.
    /// </para>
    /// </summary>
    public static void EnsureRequiredSpecifics(
        List<MappedListingSpecificDto> matched,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        TikTokAttributeValueDenylist? denylist = null,
        string? sourceCareText = null,
        string? sourceTitleText = null)
    {
        var required = RequiredSpecificAttributes(schema);
        if (required.Count == 0)
        {
            return;
        }

        foreach (var attr in required)
        {
            var filled = matched.Any(m =>
                              string.Equals(m.AttrId, attr.AttributeId, StringComparison.OrdinalIgnoreCase)
                              && !m.SkippedExisting)
                          || HasFilledConditionRow(existingByAttrId, attr.AttributeId);
            if (filled)
            {
                continue;
            }

            var value = ResolveTwinValue(attr, matched, existingByAttrId)
                        ?? ResolveRequiredDefaultValue(attr, denylist, sourceCareText, sourceTitleText);
            if (value is null)
            {
                continue;
            }

            var mapped = CreateMapped(
                attr.AttributeName,
                value.ValueName,
                attr,
                [value],
                1.0,
                "RequiredDefault",
                existingByAttrId);
            mapped.IsRequired = true;
            matched.Add(mapped);
        }
    }

    /// <summary>
    /// Value already chosen for another attribute of the same name, when this attribute's enum offers
    /// it too (TikTok gives the twins different value ids, so the name is remapped, never the id).
    /// </summary>
    private static FourSellerCategoryAttributeValueDto? ResolveTwinValue(
        FourSellerCategoryAttributeDto attr,
        IReadOnlyList<MappedListingSpecificDto> matched,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId)
    {
        if (attr.Values.Count == 0)
        {
            return null;
        }

        var attrName = NormalizeKey(attr.AttributeName);
        if (attrName.Length == 0)
        {
            return null;
        }

        var twinValueNames = matched
            .Where(m => !m.SkippedExisting
                        && !string.Equals(m.AttrId, attr.AttributeId, StringComparison.OrdinalIgnoreCase)
                        && NormalizeKey(m.AttrName) == attrName)
            .SelectMany(m => m.Values.Select(v => v.ValueName))
            .Concat(existingByAttrId.Values
                .Where(e => !string.Equals(e.AttrId, attr.AttributeId, StringComparison.OrdinalIgnoreCase)
                            && NormalizeKey(e.AttrName) == attrName)
                .SelectMany(e => (e.Values?.Select(v => v.ValueName) ?? []).Append(e.AttrValue)))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(NormalizeKey)
            .ToList();

        return twinValueNames.Count == 0
            ? null
            : attr.Values.FirstOrDefault(v => twinValueNames.Contains(NormalizeKey(v.ValueName)));
    }

    private static FourSellerCategoryAttributeValueDto? PreferByName(
        IReadOnlyList<FourSellerCategoryAttributeValueDto> candidates,
        IReadOnlyList<string> preferredKeys)
    {
        foreach (var key in preferredKeys)
        {
            var hit = candidates.FirstOrDefault(v => NormalizeKey(v.ValueName) == key);
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>Fibres that fit almost any garment, best first — used only as a mandatory fallback.</summary>
    private static readonly string[] CommonApparelMaterialKeys =
    [
        "polyester",
        "polyesterblend",
        "cotton",
        "cottonblend",
        "nylon",
        "rayon",
        "spandex",
        "viscose"
    ];

    /// <summary>
    /// Apparel fibre words a supplier title or description may claim. Order of the alternation
    /// does not matter — <see cref="FabricKeysFromText"/> returns matches in text order.
    /// </summary>
    private static readonly Regex FabricTokenRegex = new(
        @"\b(?:linen|cotton|denim|silk|satin|wool|leather|velvet|chiffon|corduroy|tweed|fleece|"
        + @"cashmere|modal|bamboo|mesh|lace|rayon|viscose|nylon|acrylic|spandex|elastane|polyester)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Normalised fibre tokens found in free text, in order of appearance. Empty when the
    /// text never names a fabric — callers then fall back to <see cref="CommonApparelMaterialKeys"/>.
    /// </summary>
    internal static IReadOnlyList<string> FabricKeysFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in FabricTokenRegex.Matches(text))
        {
            var key = NormalizeKey(match.Value);
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            keys.Add(key);
        }

        return keys;
    }

    /// <summary>True when the enum is a plain certification-style Yes/No (plus Not sure / N/A).</summary>
    private static bool IsYesNoAttribute(IReadOnlyList<FourSellerCategoryAttributeValueDto> candidates) =>
        candidates.Count is > 0 and <= 4
        && candidates.All(v => NormalizeKey(v.ValueName) is "yes" or "no" or "notsure" or "notapplicable")
        && candidates.Any(v => NormalizeKey(v.ValueName) is "no");

    /// <summary>True for the mandatory <c>Weaving method</c> of costume/apparel categories.</summary>
    public static bool IsWeavingMethodAttribute(string? name)
    {
        var n = NormalizeKey(name);
        return n is "weavingmethod" or "weavetype" or "weavemethod" or "knittingmethod";
    }

    /// <summary>True for Material and its per-part variants (outer/inner/fabric/composition).</summary>
    public static bool IsMaterialAttribute(string? name)
    {
        var n = NormalizeKey(name);
        return n is "material" or "materials" or "fabrictype" or "composition"
                or "outermaterial" or "innermaterial" or "mainmaterial" or "shellmaterial"
                or "liningmaterial" or "fabricmaterial";
    }

    /// <summary>True for <c>Country of origin</c> (sibling of Region of origin).</summary>
    public static bool IsCountryOfOriginAttribute(string? name) =>
        NormalizeKey(name) is "countryoforigin" or "countryofmanufacture";

    /// <summary>True for <c>Region of origin</c> (mandatory in costume categories such as 903824).</summary>
    public static bool IsRegionOfOriginAttribute(string? name) =>
        NormalizeKey(name) is "regionoforigin";

    /// <summary>True for the CPSIA tracking label question of children's categories.</summary>
    public static bool IsCpsiaTrackingLabelAttribute(string? name) =>
        NormalizeKey(name) is "cpsiatrackinglabel";

    /// <summary><c>Region of origin</c> of this category, when it has one.</summary>
    public static FourSellerCategoryAttributeDto? FindRegionOfOriginAttribute(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema) =>
        schema.FirstOrDefault(a => IsRegionOfOriginAttribute(a.AttributeName));

    /// <summary><c>CPSIA tracking label</c> of this category, when it has one.</summary>
    public static FourSellerCategoryAttributeDto? FindCpsiaTrackingLabelAttribute(
        IReadOnlyList<FourSellerCategoryAttributeDto> schema) =>
        schema.FirstOrDefault(a => IsCpsiaTrackingLabelAttribute(a.AttributeName));

    /// <summary>Prefers China when the category enum offers it; otherwise the first enum value.</summary>
    public static FourSellerCategoryAttributeValueDto? ResolveDefaultRegionOfOriginValue(
        FourSellerCategoryAttributeDto attr)
    {
        if (attr.Values.Count == 0)
        {
            return null;
        }

        var china = attr.Values.FirstOrDefault(v => NormalizeKey(v.ValueName) is "china");
        return china ?? attr.Values[0];
    }

    /// <summary>Prefers No, then Not applicable, else the first enum value.</summary>
    public static FourSellerCategoryAttributeValueDto? ResolveDefaultCpsiaTrackingLabelValue(
        FourSellerCategoryAttributeDto attr)
    {
        if (attr.Values.Count == 0)
        {
            return null;
        }

        var no = attr.Values.FirstOrDefault(v => NormalizeKey(v.ValueName) is "no");
        if (no is not null)
        {
            return no;
        }

        var na = attr.Values.FirstOrDefault(v =>
            NormalizeKey(v.ValueName) is "notapplicable" or "n/a" or "na");
        return na ?? attr.Values[0];
    }

    /// <summary>Picks the schema enum for a new product — prefers <c>New</c>.</summary>
    public static FourSellerCategoryAttributeValueDto? ResolveDefaultConditionTypeValue(
        FourSellerCategoryAttributeDto typeAttr)
    {
        if (typeAttr.Values.Count == 0)
        {
            return null;
        }

        var exactNew = typeAttr.Values.FirstOrDefault(v =>
            NormalizeKey(v.ValueName) is "new");
        if (exactNew is not null)
        {
            return exactNew;
        }

        return typeAttr.Values.FirstOrDefault(v =>
                   NormalizeKey(v.ValueName).Contains("new", StringComparison.Ordinal))
               ?? typeAttr.Values[0];
    }

    private static bool HasFilledConditionRow(
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        string attrId) =>
        existingByAttrId.TryGetValue(attrId, out var row)
        && !string.IsNullOrWhiteSpace(row.AttrValue);

    /// <summary>
    /// Appends default Condition type/description rows (correct TikTok ids) when the category schema
    /// requires them and the listing mirror does not already have values.
    /// </summary>
    public static List<ListingAttributeDto> MergeConditionDefaultsIntoAttributes(
        IReadOnlyList<ListingAttributeDto> existing,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        TikTokAttributeValueDenylist? denylist = null)
    {
        var typeAttr = FindConditionTypeAttribute(schema);
        var descAttr = FindConditionDescriptionAttribute(schema);
        var regionAttr = FindRegionOfOriginAttribute(schema);
        var cpsiaAttr = FindCpsiaTrackingLabelAttribute(schema);
        var washingAttr = FindWashingInstructionAttribute(schema);
        var seedWashing = washingAttr is not null && IsFashionCategory(null, schema);
        if (typeAttr is null && descAttr is null && regionAttr is null && cpsiaAttr is null && !seedWashing)
        {
            return existing.ToList();
        }

        var result = existing.ToList();
        if (typeAttr is not null && !HasFilledListingAttribute(existing, typeAttr))
        {
            var newValue = ResolveDefaultConditionTypeValue(typeAttr);
            if (newValue is not null)
            {
                result.Add(new ListingAttributeDto
                {
                    AttrId = typeAttr.AttributeId,
                    AttrName = typeAttr.AttributeName,
                    AttrValue = newValue.ValueName,
                    ValueId = newValue.ValueId,
                    Values =
                    [
                        new ListingAttributeValueDto
                        {
                            ValueId = newValue.ValueId,
                            ValueName = newValue.ValueName
                        }
                    ],
                    IsRequired = true
                });
            }
        }

        if (descAttr is not null && !HasFilledListingAttribute(existing, descAttr))
        {
            const string defaultDescription = "Brand new";
            result.Add(new ListingAttributeDto
            {
                AttrId = descAttr.AttributeId,
                AttrName = descAttr.AttributeName,
                AttrValue = defaultDescription,
                ValueId = string.Empty,
                Values =
                [
                    new ListingAttributeValueDto
                    {
                        ValueId = string.Empty,
                        ValueName = defaultDescription
                    }
                ],
                IsRequired = true
            });
        }

        AppendDefaultSpecificIfMissing(result, FindRegionOfOriginAttribute(schema), ResolveDefaultRegionOfOriginValue);
        AppendDefaultSpecificIfMissing(
            result,
            FindCpsiaTrackingLabelAttribute(schema),
            ResolveDefaultCpsiaTrackingLabelValue);
        if (seedWashing)
        {
            AppendDefaultSpecificIfMissing(
                result,
                washingAttr,
                attr => ResolveDefaultWashingInstructionValue(attr, denylist));
        }

        return result;
    }

    private static void AppendDefaultSpecificIfMissing(
        List<ListingAttributeDto> result,
        FourSellerCategoryAttributeDto? attr,
        Func<FourSellerCategoryAttributeDto, FourSellerCategoryAttributeValueDto?> resolveDefault)
    {
        if (attr is null || HasFilledListingAttribute(result, attr))
        {
            return;
        }

        var value = resolveDefault(attr);
        if (value is null)
        {
            return;
        }

        result.Add(new ListingAttributeDto
        {
            AttrId = attr.AttributeId,
            AttrName = attr.AttributeName,
            AttrValue = value.ValueName,
            ValueId = value.ValueId,
            Values =
            [
                new ListingAttributeValueDto
                {
                    ValueId = value.ValueId,
                    ValueName = value.ValueName
                }
            ],
            IsRequired = attr.IsRequired
        });
    }

    private static bool HasFilledListingAttribute(
        IReadOnlyList<ListingAttributeDto> existing,
        FourSellerCategoryAttributeDto attr)
    {
        foreach (var row in existing)
        {
            if (!IsSameAttribute(row, attr))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.AttrValue))
            {
                return true;
            }

            if (row.Values?.Any(v => !string.IsNullOrWhiteSpace(v.ValueName)) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameAttribute(
        ListingAttributeDto row,
        FourSellerCategoryAttributeDto attr)
    {
        if (!string.IsNullOrWhiteSpace(row.AttrId)
            && string.Equals(row.AttrId.Trim(), attr.AttributeId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(row.AttrName)
               && !string.IsNullOrWhiteSpace(attr.AttributeName)
               && string.Equals(
                   NormalizeKey(row.AttrName),
                   NormalizeKey(attr.AttributeName),
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps a care/washing source pair onto the schema Washing Instruction attribute.
    /// Enum match only when the category exposes options, because a value name without a matching
    /// enum id reaches TikTok as a custom value and <c>100401</c> refuses those.
    /// </summary>
    public static MappedListingSpecificDto? TryMapOntoWashingInstruction(
        string sourceName,
        string sourceValue,
        FourSellerCategoryAttributeDto washingAttr,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        TikTokAttributeValueDenylist? denylist = null)
    {
        if (string.IsNullOrWhiteSpace(sourceValue))
        {
            return null;
        }

        var best = FindBestWashingEnumMatch(
            sourceValue,
            washingAttr.Values,
            washingAttr.AttributeId,
            denylist);
        if (best is not null)
        {
            var (enumValue, score) = best.Value;
            var mapped = CreateMapped(
                sourceName,
                sourceValue,
                washingAttr,
                [enumValue],
                score,
                score >= 0.999 ? "WashingExact" : "WashingFuzzy",
                existingByAttrId);
            mapped.IsRequired = true;
            return mapped;
        }

        // Never invent a custom valueName when TikTok already lists enums for this attr.
        // Care phrases that match no enum (Do not bleach) fall back to a washing method.
        if (washingAttr.Values.Count > 0)
        {
            if (LooksLikeWashCareValue(sourceValue))
            {
                var fallback = ResolveDefaultWashingInstructionValue(washingAttr, denylist, sourceValue);
                if (fallback is not null)
                {
                    var mapped = CreateMapped(
                        sourceName,
                        sourceValue,
                        washingAttr,
                        [fallback],
                        0.85,
                        "WashingDefault",
                        existingByAttrId);
                    mapped.IsRequired = true;
                    return mapped;
                }
            }

            return null;
        }

        // Schema has no enum list — free-text only when the category marks the attr customizable.
        if (!washingAttr.IsCustomizable)
        {
            return null;
        }

        var custom = CreateMapped(
            sourceName,
            sourceValue,
            washingAttr,
            [
                new FourSellerCategoryAttributeValueDto
                {
                    ValueId = string.Empty,
                    ValueName = sourceValue.Trim()
                }
            ],
            0.9,
            "WashingCustom",
            existingByAttrId);
        custom.IsRequired = true;
        return custom;
    }

    /// <summary>
    /// Best enum option for a care/washing free-text string, or null when nothing clears the
    /// confidence floor (strips trailing "only" / punctuation for synonym hits like
    /// <c>Hand wash only</c> → <c>Hand wash</c>).
    /// </summary>
    public static FourSellerCategoryAttributeValueDto? ResolveBestWashingEnumValue(
        string? sourceValue,
        IReadOnlyList<FourSellerCategoryAttributeValueDto> enums,
        string? attributeId = null,
        TikTokAttributeValueDenylist? denylist = null)
    {
        var hit = FindBestWashingEnumMatch(sourceValue, enums, attributeId, denylist);
        return hit?.Value;
    }

    private static (FourSellerCategoryAttributeValueDto Value, double Score)? FindBestWashingEnumMatch(
        string? sourceValue,
        IReadOnlyList<FourSellerCategoryAttributeValueDto> enums,
        string? attributeId = null,
        TikTokAttributeValueDenylist? denylist = null)
    {
        if (string.IsNullOrWhiteSpace(sourceValue) || enums.Count == 0)
        {
            return null;
        }

        var allowed = enums
            .Where(v => denylist?.IsRejected(attributeId, v.ValueName) != true)
            .ToList();
        if (allowed.Count == 0)
        {
            return null;
        }

        FourSellerCategoryAttributeValueDto? bestValue = null;
        var bestScore = 0.0;
        foreach (var candidate in ExpandWashingSourceVariants(sourceValue))
        {
            foreach (var value in allowed)
            {
                var score = WashingValueScore(candidate, value.ValueName);
                if (score < MinWashingMapConfidence)
                {
                    continue;
                }

                if (bestValue is null || score > bestScore)
                {
                    bestValue = value;
                    bestScore = score;
                }
            }
        }

        return bestValue is null ? null : (bestValue, bestScore);
    }

    /// <summary>Source plus shortened forms (drop trailing "only", take first clause).</summary>
    private static IEnumerable<string> ExpandWashingSourceVariants(string sourceValue)
    {
        var trimmed = sourceValue.Trim();
        yield return trimmed;

        var firstClause = trimmed.Split([',', ';', '/', '|'], 2, StringSplitOptions.TrimEntries)[0];
        if (!string.Equals(firstClause, trimmed, StringComparison.OrdinalIgnoreCase)
            && firstClause.Length > 0)
        {
            yield return firstClause;
        }

        var withoutOnly = System.Text.RegularExpressions.Regex.Replace(
            firstClause,
            @"\bonly\b",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        withoutOnly = System.Text.RegularExpressions.Regex.Replace(withoutOnly, @"\s{2,}", " ").Trim();
        if (withoutOnly.Length > 0
            && !string.Equals(withoutOnly, firstClause, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(withoutOnly, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            yield return withoutOnly;
        }
    }

    private static bool LooksLikeWashCareValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var n = value.Trim().ToLowerInvariant();
        return n.Contains("wash", StringComparison.Ordinal)
               || n.Contains("dry clean", StringComparison.Ordinal)
               || n.Contains("hand wash", StringComparison.Ordinal)
               || n.Contains("machine wash", StringComparison.Ordinal)
               || n.Contains("laundry", StringComparison.Ordinal);
    }

    private static double WashingValueScore(string sourceValue, string enumValueName)
    {
        var sourceNorm = NormalizeKey(sourceValue);
        var enumNorm = NormalizeKey(enumValueName);
        if (sourceNorm.Length == 0 || enumNorm.Length == 0)
        {
            return 0;
        }

        if (sourceNorm == enumNorm)
        {
            return 1;
        }

        // "Do not dry clean" contains "Dry clean" and shares every distinctive token with it, so both
        // scoring paths below rank that pair near the top — while publishing it would tell the buyer
        // to do the one thing the supplier forbade.
        if (ForbidsTreatment(sourceNorm, enumNorm) || ForbidsTreatment(enumNorm, sourceNorm))
        {
            return 0;
        }

        // Containment without the global 0.55 length ratio — care text is usually much longer.
        if (sourceNorm.Contains(enumNorm, StringComparison.Ordinal)
            || enumNorm.Contains(sourceNorm, StringComparison.Ordinal))
        {
            var shorter = Math.Min(sourceNorm.Length, enumNorm.Length);
            var longer = Math.Max(sourceNorm.Length, enumNorm.Length);
            var ratio = shorter / (double)longer;
            return Math.Clamp(0.88 + (0.1 * ratio), 0, 1);
        }

        var wordScore = WordOverlapScore(WordTokens(sourceValue), WordTokens(enumValueName));
        // Boost only when a distinctive care-mode token is shared — bare "wash" alone is too weak
        // (e.g. "Totally invented wash ritual" must not map onto Hand wash / Machine wash).
        var sourceWords = WordTokens(sourceValue);
        var enumWords = WordTokens(enumValueName);
        if (DistinctiveWashModeTokens.Any(t => sourceWords.Contains(t) && enumWords.Contains(t)))
        {
            wordScore = Math.Max(wordScore, 0.82);
        }

        return Math.Max(wordScore, SimilarityScore(sourceValue, enumValueName));
    }

    /// <summary>
    /// True when <paramref name="negatingNorm"/> forbids exactly the treatment
    /// <paramref name="treatmentNorm"/> prescribes. Both arguments must already be normalized, which is
    /// what lets the negation sit directly in front of the treatment (<c>donot</c> + <c>dryclean</c>).
    /// </summary>
    private static bool ForbidsTreatment(string negatingNorm, string treatmentNorm)
    {
        if (treatmentNorm.Length == 0 || IsNegativeCareValueName(treatmentNorm))
        {
            return false;
        }

        return CareNegationPrefixes.Any(prefix =>
            negatingNorm.Contains(prefix + treatmentNorm, StringComparison.Ordinal));
    }

    /// <summary>Normalizes a label for fuzzy compare.</summary>
    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lower = value.Trim().ToLowerInvariant();
        lower = NonAlphaNumRegex.Replace(lower, string.Empty);
        return lower;
    }

    /// <summary>True when the attribute belongs on the Product Certification card, not Specifics.</summary>
    public static bool IsCertificationAttribute(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var n = name.Trim().ToLowerInvariant();
        return n.Contains("prop 65", StringComparison.Ordinal)
               || n.Contains("prop65", StringComparison.Ordinal)
               || n.Contains("hazardous", StringComparison.Ordinal)
               || n.Contains("dangerous goods", StringComparison.Ordinal)
               || n.Contains("carcinogen", StringComparison.Ordinal)
               || n.Contains("reproductive", StringComparison.Ordinal)
               || n.Contains("reprotoxic", StringComparison.Ordinal)
               || n.Contains("repro. chems", StringComparison.Ordinal)
               || n.Contains("repro chems", StringComparison.Ordinal);
    }

    /// <summary>
    /// Legacy no-op: Region of origin / CPSIA are mandatory on many categories and must be filled
    /// (see <see cref="EnsureRegionAndCpsiaSpecifics"/>). Kept so older call sites compile.
    /// </summary>
    public static bool IsAutoPublishBlockedSpecificAttribute(string? name) => false;

    private static bool ShouldSkipSourceKey(string name)
    {
        if (SkipSourceKeys.Contains(name.Trim()))
        {
            return true;
        }

        var n = name.Trim().ToLowerInvariant();
        return n is "brand"
               || IsCertificationAttribute(name);
    }

    private static List<(FourSellerCategoryAttributeDto Attr, FourSellerCategoryAttributeValueDto Value, string Norm)>
        BuildValueIndex(IReadOnlyList<FourSellerCategoryAttributeDto> schema)
    {
        var list = new List<(FourSellerCategoryAttributeDto, FourSellerCategoryAttributeValueDto, string)>();
        foreach (var attr in schema)
        {
            if (IsCertificationAttribute(attr.AttributeName))
            {
                continue;
            }

            foreach (var v in attr.Values)
            {
                var norm = NormalizeKey(v.ValueName);
                if (norm.Length == 0)
                {
                    continue;
                }

                list.Add((attr, v, norm));
            }
        }

        return list;
    }

    private static List<ValueHit> FindValueHits(
        string sourceValue,
        List<(FourSellerCategoryAttributeDto Attr, FourSellerCategoryAttributeValueDto Value, string Norm)> index,
        ISet<string> usedAttrIds)
    {
        var sourceNorm = NormalizeKey(sourceValue);
        if (sourceNorm.Length == 0)
        {
            return [];
        }

        var exact = new List<ValueHit>();
        foreach (var (attr, value, norm) in index)
        {
            if (usedAttrIds.Contains(attr.AttributeId))
            {
                continue;
            }

            if (string.Equals(norm, sourceNorm, StringComparison.Ordinal))
            {
                exact.Add(new ValueHit
                {
                    Attr = attr,
                    Value = value,
                    Score = 1.0,
                    Method = "Exact"
                });
            }
        }

        if (exact.Count > 0)
        {
            return exact;
        }

        // Containment / fuzzy — keep only strong hits.
        var fuzzy = new List<ValueHit>();
        foreach (var (attr, value, norm) in index)
        {
            if (usedAttrIds.Contains(attr.AttributeId))
            {
                continue;
            }

            double score;
            if (sourceNorm.Contains(norm, StringComparison.Ordinal)
                || norm.Contains(sourceNorm, StringComparison.Ordinal))
            {
                var containment = Math.Min(sourceNorm.Length, norm.Length)
                                  / (double)Math.Max(sourceNorm.Length, norm.Length);
                // Require the shorter side to be substantial (≥ 60% of longer) to avoid
                // "No" matching everything containing "No", or "Regular" matching "Irregular".
                if (containment < 0.55)
                {
                    continue;
                }

                // Prefer distinctive shared tokens for containment boost.
                var wordScore = WordOverlapScore(WordTokens(sourceValue), WordTokens(value.ValueName));
                score = Math.Max(0.82 + (0.15 * containment), wordScore);
            }
            else
            {
                score = SimilarityScore(sourceValue, value.ValueName);
            }

            if (score >= MinValueFuzzyScore)
            {
                fuzzy.Add(new ValueHit
                {
                    Attr = attr,
                    Value = value,
                    Score = score,
                    Method = "Fuzzy"
                });
            }
        }

        // Keep the best hit per attribute, then the top score band (±0.05) to detect collisions.
        if (fuzzy.Count == 0)
        {
            return [];
        }

        var bestPerAttr = fuzzy
            .GroupBy(h => h.Attr.AttributeId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(h => h.Score).First())
            .OrderByDescending(h => h.Score)
            .ToList();

        var top = bestPerAttr[0].Score;
        return bestPerAttr.Where(h => h.Score >= top - 0.05).ToList();
    }

    /// <summary>
    /// Combined similarity tolerant to extra descriptive words
    /// (e.g. "Regular Fit" ↔ "Regular").
    /// </summary>
    private static double SimilarityScore(string aRaw, string bRaw)
    {
        var aNorm = NormalizeKey(aRaw);
        var bNorm = NormalizeKey(bRaw);
        if (aNorm.Length == 0 || bNorm.Length == 0)
        {
            return 0;
        }

        if (aNorm == bNorm)
        {
            return 1;
        }

        var wordScore = WordOverlapScore(WordTokens(aRaw), WordTokens(bRaw));
        var bigramScore = TokenOverlapScore(aNorm, bNorm);
        return Math.Max(wordScore, bigramScore);
    }

    private static HashSet<string> WordTokens(string raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return set;
        }

        foreach (var token in raw.Split(
                     [' ', '\t', '\n', '\r', '-', '_', '/', '\\', '&', ',', '.', '(', ')', ':'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim().ToLowerInvariant();
            if (t.Length > 0)
            {
                set.Add(t);
            }
        }

        return set;
    }

    private static double WordOverlapScore(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var inter = a.Count(b.Contains);
        if (inter == 0)
        {
            return 0;
        }

        var smaller = Math.Min(a.Count, b.Count);
        var larger = Math.Max(a.Count, b.Count);
        var sharedTokens = a.Where(b.Contains).ToList();
        var hasDistinctiveShared = sharedTokens.Any(t => !GenericTokens.Contains(t));
        if (inter == smaller && hasDistinctiveShared)
        {
            return 0.8 + (0.15 * (smaller / (double)larger));
        }

        var union = a.Count + b.Count - inter;
        return inter / (double)union;
    }

    private static double TokenOverlapScore(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        static HashSet<string> Bigrams(string s)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (s.Length < 2)
            {
                set.Add(s);
                return set;
            }

            for (var i = 0; i < s.Length - 1; i++)
            {
                set.Add(s.Substring(i, 2));
            }

            return set;
        }

        var ba = Bigrams(a);
        var bb = Bigrams(b);
        if (ba.Count == 0 || bb.Count == 0)
        {
            return 0;
        }

        var inter = ba.Count(bb.Contains);
        return 2.0 * inter / (ba.Count + bb.Count);
    }

    private static string ToPlainMultiline(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = html
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);

        text = TagRegex.Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        var lines = text.Split('\n');
        return string.Join(
            "\n",
            lines.Select(l => MultiSpaceRegex.Replace(l, " ").Trim()));
    }

    private static string ExtractJsonArray(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = trimmed.IndexOf('\n');
            if (firstNl >= 0)
            {
                trimmed = trimmed[(firstNl + 1)..];
            }

            var fence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                trimmed = trimmed[..fence];
            }

            trimmed = trimmed.Trim();
        }

        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }

    private static string? ReadJsonString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var p))
            {
                continue;
            }

            if (p.ValueKind == JsonValueKind.String)
            {
                return p.GetString();
            }

            if (p.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return p.ToString();
            }
        }

        return null;
    }

    private static double? ReadJsonDouble(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var p))
            {
                continue;
            }

            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d))
            {
                return d;
            }

            if (p.ValueKind == JsonValueKind.String
                && double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
