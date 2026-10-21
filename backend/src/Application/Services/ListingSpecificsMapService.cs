using System.Text;
using System.Text.Json;
using Application.DTOs.FourSeller;
using Application.DTOs.Listing;
using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// Orchestrates description parse → 4Seller category schema → value-first / AI Specifics mapping.
/// </summary>
public class ListingSpecificsMapService : IListingSpecificsMapService
{
    private readonly IApplicationDbContext _db;
    private readonly ITikTokCategorySchemaProvider _categorySchema;
    private readonly IOpenAiService _openAi;
    private readonly ITikTokAttributeRejectionService _attributeRejections;
    private readonly ILogger<ListingSpecificsMapService> _logger;

    /// <summary>Creates the Specifics mapping service.</summary>
    public ListingSpecificsMapService(
        IApplicationDbContext db,
        ITikTokCategorySchemaProvider categorySchema,
        IOpenAiService openAi,
        ITikTokAttributeRejectionService attributeRejections,
        ILogger<ListingSpecificsMapService> logger)
    {
        _db = db;
        _categorySchema = categorySchema;
        _openAi = openAi;
        _attributeRejections = attributeRejections;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MapListingSpecificsResultDto> MapAsync(
        Guid listingId,
        MapListingSpecificsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new BusinessException("Body map Specifics là bắt buộc.");
        }

        var categoryId = (request.CategoryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            throw new BusinessException("Chọn Category trước khi Auto map Specifics.");
        }

        var listing = await _db.Listings
            .AsNoTracking()
            .Where(l => l.Id == listingId)
            .Select(l => new
            {
                l.Id,
                l.OwnerId,
                l.ShopId,
                l.Site,
                l.Platform,
                l.SupplierDescriptionHtml,
                l.DescriptionHtml,
                l.DescriptionRewritten
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        // The supplier snapshot is the source of truth, not the editor contents: a rewrite
        // replaces the Key: Value rows with prose, which is why Auto Specifics used to have to
        // run first. Reading the snapshot removes that ordering constraint entirely.
        var description = FirstNonEmpty(
            listing.SupplierDescriptionHtml,
            listing.DescriptionRewritten ? null : listing.DescriptionHtml);

        var pairs = ListingSpecificsMappingEngine.ParseDescriptionPairs(description);
        if (pairs.Count == 0)
        {
            // Cleaned / rewritten supplier copy often has no Key: Value rows left. Mapping still has to
            // run: a category can require Specifics the description never mentions (903824 demands
            // Weaving method / Region of origin / Material), and leaving them empty makes 4Seller answer
            // 【Publish Failed】Specifics.<attribute name>.
            _logger.LogInformation(
                "Auto Specifics listing {ListingId}: no Key: Value pairs in supplier description — "
                + "chỉ seed các attribute bắt buộc của category",
                listingId);
        }

        var localShopId = request.ShopId ?? listing.ShopId
            ?? throw new BusinessException("Chọn shop Publish trước khi Auto map Specifics.");

        var shop = await _db.Shops
            .AsNoTracking()
            .Where(s => s.Id == localShopId)
            .Select(s => new
            {
                s.Id,
                s.OwnerId,
                s.FourSellerShopId,
                PlatformCode = s.Platform != null ? s.Platform.Code : null
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessException("Không tìm thấy shop.");

        if (shop.OwnerId != listing.OwnerId)
        {
            throw new BusinessException("Shop không thuộc seller của listing.");
        }

        if (!string.IsNullOrWhiteSpace(listing.Platform) &&
            !string.IsNullOrWhiteSpace(shop.PlatformCode) &&
            !string.Equals(shop.PlatformCode, listing.Platform, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Shop không cùng platform với listing.");
        }

        if (string.IsNullOrWhiteSpace(shop.FourSellerShopId) ||
            !long.TryParse(shop.FourSellerShopId, out var fourSellerShopId) ||
            fourSellerShopId <= 0)
        {
            throw new BusinessException(
                "Shop chưa có 4Seller ID — kết nối shop với 4Seller trước khi map Specifics.");
        }

        var site = string.IsNullOrWhiteSpace(listing.Site) ? "US" : listing.Site.Trim();
        var schemaRaw = await _categorySchema.GetAsync(
            listing.OwnerId,
            categoryId,
            fourSellerShopId,
            site,
            cancellationToken);

        // Default cert Yes/No = No → conditional Carcinogen / Reprotoxic attrs stay excluded.
        var satisfiedConditions = ListingSpecificsMappingEngine.DefaultCertificationAnswers();

        // 4Seller still offers values TikTok has withdrawn, so drop the ones a publish already proved
        // are refused before anything — matching, AI or the default seed — can pick them.
        var denylist = await _attributeRejections.GetDenylistAsync(categoryId, cancellationToken);
        var schema = ListingSpecificsMappingEngine.FilterSchemaForSpecificsMapping(
            schemaRaw,
            satisfiedConditions,
            denylist);

        if (schema.Count == 0)
        {
            throw new BusinessException(
                "4Seller không trả về attribute schema cho category này. Thử đổi category hoặc sync lại.");
        }

        var existingByAttrId = BuildExistingLookup(request.ExistingAttributes);

        var (matched, collisions, remaining) = ListingSpecificsMappingEngine.MatchValueFirst(
            pairs,
            schema,
            existingByAttrId);

        var usedAttrIds = new HashSet<string>(
            matched.Select(m => m.AttrId),
            StringComparer.OrdinalIgnoreCase);

        var unmatched = new List<UnmappedListingSpecificDto>();

        // 1) AI resolves value collisions (same value under multiple attributes).
        if (collisions.Count > 0)
        {
            try
            {
                var aiCollisionMatched = await TryAiResolveCollisionsAsync(
                    request.Title,
                    request.CategoryName,
                    collisions,
                    schema,
                    existingByAttrId,
                    usedAttrIds,
                    cancellationToken);
                matched.AddRange(aiCollisionMatched);

                var resolvedKeys = new HashSet<string>(
                    aiCollisionMatched.Select(m => ListingSpecificsMappingEngine.NormalizeKey(m.SourceName)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var c in collisions)
                {
                    if (resolvedKeys.Contains(ListingSpecificsMappingEngine.NormalizeKey(c.SourceName)))
                    {
                        continue;
                    }

                    unmatched.Add(new UnmappedListingSpecificDto
                    {
                        SourceName = c.SourceName,
                        SourceValue = c.SourceValue,
                        Reason =
                            $"Value khớp nhiều attribute ({string.Join(", ", c.Candidates.Select(x => x.Attr.AttributeName))}); AI chưa chọn được."
                    });
                }
            }
            catch (BusinessException ex)
            {
                _logger.LogInformation(ex, "AI collision resolution skipped/failed");
                foreach (var c in collisions)
                {
                    unmatched.Add(new UnmappedListingSpecificDto
                    {
                        SourceName = c.SourceName,
                        SourceValue = c.SourceValue,
                        Reason =
                            $"Value khớp nhiều attribute; AI không dùng được ({ex.Message})"
                    });
                }
            }
        }

        // 2) AI for leftover pairs with no value hit.
        if (remaining.Count > 0)
        {
            try
            {
                var aiMatched = await TryAiMapRemainingAsync(
                    request.Title,
                    request.CategoryName,
                    remaining,
                    schema,
                    existingByAttrId,
                    usedAttrIds,
                    cancellationToken);
                matched.AddRange(aiMatched);

                var aiSourceKeys = new HashSet<string>(
                    aiMatched.Select(m => ListingSpecificsMappingEngine.NormalizeKey(m.SourceName)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var (name, value) in remaining)
                {
                    if (aiSourceKeys.Contains(ListingSpecificsMappingEngine.NormalizeKey(name)))
                    {
                        continue;
                    }

                    unmatched.Add(new UnmappedListingSpecificDto
                    {
                        SourceName = name,
                        SourceValue = value,
                        Reason = "Không khớp đủ tin cậy với valueList schema category."
                    });
                }
            }
            catch (BusinessException ex)
            {
                _logger.LogInformation(ex, "AI Specifics mapping skipped/failed; returning value-first only");
                foreach (var (name, value) in remaining)
                {
                    unmatched.Add(new UnmappedListingSpecificDto
                    {
                        SourceName = name,
                        SourceValue = value,
                        Reason = $"Chưa map value-first; AI không dùng được ({ex.Message})"
                    });
                }
            }
        }

        // Keep all accepted matches (≥ MinAcceptConfidence) in Matched for review.
        // FE auto-selects only ≥ MinAutoFillConfidence (85%); weaker rows stay visible/opt-in.

        // Fashion / apparel: always map Care Instructions → 4Seller Washing Instruction when possible.
        ListingSpecificsMappingEngine.EnsureFashionWashingInstruction(
            matched,
            unmatched,
            pairs,
            schema,
            existingByAttrId,
            request.CategoryName,
            denylist);

        ListingSpecificsMappingEngine.AlignWashingWithCareInstructions(
            matched,
            schema,
            existingByAttrId,
            denylist);

        ListingSpecificsMappingEngine.EnsureConditionSpecifics(
            matched,
            schema,
            existingByAttrId);
        ListingSpecificsMappingEngine.EnsureRegionAndCpsiaSpecifics(
            matched,
            schema,
            existingByAttrId);

        // Whatever the description did not cover, the category may still insist on. Runs last so every
        // value the supplier actually stated wins over a default.
        ListingSpecificsMappingEngine.EnsureRequiredSpecifics(
            matched,
            schema,
            existingByAttrId,
            denylist,
            pairs
                .Where(p => ListingSpecificsMappingEngine.IsCareInstructionSourceKey(p.Name))
                .Select(p => p.Value)
                .FirstOrDefault(),
            request.Title);

        var washingAttr = ListingSpecificsMappingEngine.FindWashingInstructionAttribute(schema);
        var forceRequiredIds = washingAttr is not null
                               && ListingSpecificsMappingEngine.IsFashionCategory(request.CategoryName, schema)
            ? new[] { washingAttr.AttributeId }
            : Array.Empty<string>();

        var filledIds = matched
            .Where(m => !m.SkippedExisting)
            .Select(m => m.AttrId)
            .Concat(existingByAttrId
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.AttrValue))
                .Select(kv => kv.Key));

        return new MapListingSpecificsResultDto
        {
            Matched = matched
                .OrderByDescending(m => m.Confidence)
                .ThenBy(m => m.AttrName)
                .ToList(),
            Unmatched = unmatched,
            MissingRequired = ListingSpecificsMappingEngine.BuildMissingRequired(
                schema,
                filledIds,
                forceRequiredIds),
            SchemaAttributeCount = schema.Count,
            ParsedPairCount = pairs.Count
        };
    }

    private async Task<List<MappedListingSpecificDto>> TryAiResolveCollisionsAsync(
        string? title,
        string? categoryName,
        List<ListingSpecificsMappingEngine.ValueCollision> collisions,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        HashSet<string> usedAttrIds,
        CancellationToken cancellationToken)
    {
        var payload = collisions.Select(c => new
        {
            sourceName = c.SourceName,
            sourceValue = c.SourceValue,
            candidates = c.Candidates
                .Where(h => !usedAttrIds.Contains(h.Attr.AttributeId))
                .Select(h => new
                {
                    attributeId = h.Attr.AttributeId,
                    attributeName = h.Attr.AttributeName,
                    isMultiple = h.Attr.IsMultiple,
                    valueId = h.Value.ValueId,
                    valueName = h.Value.ValueName,
                    score = h.Score
                })
                .ToList()
        }).Where(x => x.candidates.Count > 0).ToList();

        if (payload.Count == 0)
        {
            return [];
        }

        var systemPrompt =
            "You resolve ambiguous marketplace attribute mappings. "
            + "Each source pair's value appears under MULTIPLE category attributes. "
            + "Pick exactly ONE candidate per source pair using the source key meaning. "
            + "When the chosen attribute has isMultiple=true and the source lists several values "
            + "(comma/slash/and), return values:[{valueId,valueName},…] (max 5) from that attribute only. "
            + "Otherwise return a single valueId/valueName. "
            + "Return ONLY a JSON array. Each item: "
            + "{\"sourceName\",\"sourceValue\",\"attributeId\",\"valueId\",\"valueName\",\"values\",\"confidence\"}. "
            + "Rules: attributeId and every valueId MUST be from that pair's candidates list. "
            + "confidence is 0-1. Skip uncertain pairs. No markdown.";

        var userSb = new StringBuilder();
        userSb.AppendLine($"Title: {title ?? string.Empty}");
        userSb.AppendLine($"Category: {categoryName ?? string.Empty}");
        userSb.AppendLine("Collisions:");
        userSb.AppendLine(JsonSerializer.Serialize(payload));

        var aiJson = await _openAi.CompleteJsonAsync(systemPrompt, userSb.ToString(), cancellationToken);

        var allowed = collisions.ToDictionary(
            c => ListingSpecificsMappingEngine.NormalizeKey(c.SourceName),
            c => (IReadOnlySet<string>)c.Candidates
                .Select(h => h.Attr.AttributeId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var remainingPairs = collisions
            .Select(c => (c.SourceName, c.SourceValue))
            .ToList();

        return ListingSpecificsMappingEngine.ValidateAiSuggestions(
            aiJson,
            remainingPairs,
            schema,
            existingByAttrId,
            usedAttrIds,
            matchMethod: "AiCollision",
            allowedAttrIdsBySource: allowed);
    }

    private async Task<List<MappedListingSpecificDto>> TryAiMapRemainingAsync(
        string? title,
        string? categoryName,
        List<(string Name, string Value)> remaining,
        IReadOnlyList<FourSellerCategoryAttributeDto> schema,
        IReadOnlyDictionary<string, UpdateListingMirrorAttributeDto> existingByAttrId,
        HashSet<string> usedAttrIds,
        CancellationToken cancellationToken)
    {
        // Prefer compact attrs still free. Do not truncate enum lists of small attrs;
        // for huge lists keep a name-prefix sample only as last resort.
        // Prefer fashion-useful Optional attrs; always include isMultiple for the model.
        var fashionPrefer = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "material", "pattern", "neckline", "sleeve length", "style", "season", "fit",
            "fabric type", "fabric weight", "care instructions", "washing instructions",
            "washing instruction", "closure type", "occasion", "clothing length", "top length",
            "sleeve type", "back style", "collar type", "feature", "design", "embellishment",
            "weave type", "silhouette", "apparel fabric stretch", "lifestyle", "hemline form"
        };

        var available = schema
            .Where(a => !usedAttrIds.Contains(a.AttributeId))
            .OrderByDescending(a => a.IsRequired)
            .ThenByDescending(a => fashionPrefer.Contains(a.AttributeName.Trim()))
            .ThenBy(a => a.Values.Count > 200 ? 1 : 0)
            .Select(a =>
            {
                var values = a.Values;
                IEnumerable<object> valuePayload;
                if (values.Count <= 80)
                {
                    valuePayload = values.Select(v => (object)new { valueId = v.ValueId, valueName = v.ValueName });
                }
                else
                {
                    // Sample + any values whose name shares a token with remaining source values.
                    var needles = remaining
                        .SelectMany(r => ListingSpecificsMappingEngine.SplitMultiValueTokens(r.Value)
                            .Append(r.Value))
                        .Select(ListingSpecificsMappingEngine.NormalizeKey)
                        .Where(n => n.Length >= 3)
                        .ToHashSet(StringComparer.Ordinal);

                    var relevant = values
                        .Where(v =>
                        {
                            var n = ListingSpecificsMappingEngine.NormalizeKey(v.ValueName);
                            return needles.Any(needle =>
                                n.Contains(needle, StringComparison.Ordinal)
                                || needle.Contains(n, StringComparison.Ordinal));
                        })
                        .Take(40)
                        .ToList();

                    // No relevant needle on huge enums → omit values entirely (AI should skip).
                    if (relevant.Count == 0)
                    {
                        valuePayload = Array.Empty<object>();
                    }
                    else
                    {
                        valuePayload = relevant
                            .GroupBy(v => v.ValueId, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.First())
                            .Take(60)
                            .Select(v => (object)new { valueId = v.ValueId, valueName = v.ValueName });
                    }
                }

                return new
                {
                    attributeId = a.AttributeId,
                    attributeName = a.AttributeName,
                    isRequired = a.IsRequired,
                    isMultiple = a.IsMultiple,
                    isCustomizable = a.IsCustomizable,
                    values = valuePayload.ToList()
                };
            })
            .Where(a => a.values.Count > 0 || a.isCustomizable)
            .Take(100)
            .ToList();

        var systemPrompt =
            "You map marketplace product attribute key/value pairs onto a TikTok Shop category schema. "
            + "Prefer matching the source VALUE against the provided valueList, then confirm with the source KEY. "
            + "When isMultiple=true and the source lists several values (comma/slash/and), return "
            + "values:[{valueId,valueName},…] with every id from that attribute's valueList (max 5). "
            + "When isMultiple=false return exactly one value. "
            + "Return ONLY a JSON array. Each item: "
            + "{\"sourceName\",\"sourceValue\",\"attributeId\",\"valueId\",\"valueName\",\"values\",\"confidence\"}. "
            + "Rules: attributeId MUST be from the provided schema; every valueId MUST be from that attribute's values "
            + "unless isCustomizable is true (then valueId may be empty). "
            + "Do not invent ids. confidence is 0-1. Skip uncertain pairs. No markdown.";

        var userSb = new StringBuilder();
        userSb.AppendLine($"Title: {title ?? string.Empty}");
        userSb.AppendLine($"Category: {categoryName ?? string.Empty}");
        userSb.AppendLine("Source pairs:");
        userSb.AppendLine(JsonSerializer.Serialize(remaining.Select(p => new { name = p.Name, value = p.Value })));
        userSb.AppendLine("Schema:");
        userSb.AppendLine(JsonSerializer.Serialize(available));

        var aiJson = await _openAi.CompleteJsonAsync(systemPrompt, userSb.ToString(), cancellationToken);
        return ListingSpecificsMappingEngine.ValidateAiSuggestions(
            aiJson,
            remaining,
            schema,
            existingByAttrId,
            usedAttrIds,
            matchMethod: "Ai");
    }

    private static Dictionary<string, UpdateListingMirrorAttributeDto> BuildExistingLookup(
        List<UpdateListingMirrorAttributeDto>? existing)
    {
        var map = new Dictionary<string, UpdateListingMirrorAttributeDto>(StringComparer.OrdinalIgnoreCase);
        if (existing is null)
        {
            return map;
        }

        foreach (var a in existing)
        {
            if (string.IsNullOrWhiteSpace(a.AttrId))
            {
                continue;
            }

            map[a.AttrId.Trim()] = a;
        }

        return map;
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;
}
