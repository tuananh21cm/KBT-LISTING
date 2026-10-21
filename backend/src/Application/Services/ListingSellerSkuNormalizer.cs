using System.Globalization;
using System.Text.Json;
using Application.DTOs.Listing;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// Dedupes sale-prop combos and ensures unique sellerSku values for listing variants.
/// </summary>
internal static class ListingSellerSkuNormalizer
{
    private static readonly char[] SkuAlphanum =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    public static bool HasUniqueSellerSkus(IEnumerable<ListingVariationDto> variations)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in variations)
        {
            var sku = row.SellerSku?.Trim();
            if (string.IsNullOrWhiteSpace(sku))
            {
                return false;
            }

            if (!seen.Add(sku))
            {
                return false;
            }
        }

        return true;
    }

    public static void EnsureUniqueSellerSkus(ListingDetailDto detail)
    {
        DedupeVariationDtos(detail.Variations);
        FillAndUniqueSellerSkus(
            detail.Variations,
            NormalizeNumericSkuPrefix(detail.FourSellerListingId.ToString()));
    }

    public static bool DedupeListingVariations(
        Listing listing,
        ILogger? logger = null,
        Action<ListingVariation>? onRemove = null)
    {
        var variations = listing.Variations;
        if (variations.Count == 0)
        {
            return false;
        }

        var changed = false;
        var listingId = listing.Id;
        var ordered = variations
            .OrderBy(v => v.FourSellerSkuId is > 0 ? 0 : 1)
            .ThenBy(v => v.CreatedAt)
            .ToList();

        var keptByCombo = new Dictionary<string, ListingVariation>(StringComparer.Ordinal);
        var remove = new List<ListingVariation>();

        foreach (var row in ordered)
        {
            var combo = ResolveComboKey(row.Title, row.VariantAttributes);
            if (combo is null)
            {
                continue;
            }

            if (keptByCombo.TryGetValue(combo, out var first))
            {
                remove.Add(row);
                logger?.LogInformation(
                    "Listing {ListingId}: bỏ variant trùng combo {Combo} (giữ {KeptId}, bỏ {RemovedId})",
                    listingId,
                    combo,
                    first.Id,
                    row.Id);
                continue;
            }

            keptByCombo[combo] = row;
        }

        foreach (var row in remove)
        {
            variations.Remove(row);
            onRemove?.Invoke(row);
            changed = true;
        }

        if (HasUniqueEntitySkus(variations) && !changed)
        {
            return false;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in variations.OrderBy(v => v.FourSellerSkuId is > 0 ? 0 : 1).ThenBy(v => v.CreatedAt))
        {
            var sku = row.SellerSku?.Trim();
            if (string.IsNullOrWhiteSpace(sku) || !used.Add(sku))
            {
                var previous = sku;
                sku = GenerateUniqueSku(row, used, prefix: null);
                row.SellerSku = sku;
                if (row.FourSellerSkuId is > 0 && !string.Equals(previous, sku, StringComparison.OrdinalIgnoreCase))
                {
                    row.FourSellerSkuId = null;
                }

                logger?.LogInformation(
                    "Listing {ListingId}: đổi sellerSku {OldSku} → {NewSku} (variant {VariantId})",
                    listingId,
                    previous ?? "(empty)",
                    sku,
                    row.Id);
                changed = true;
            }
        }

        return changed;
    }

    private static void DedupeVariationDtos(List<ListingVariationDto> variations)
    {
        if (variations.Count <= 1)
        {
            return;
        }

        var keptCombos = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<ListingVariationDto>(variations.Count);
        foreach (var row in variations
                     .OrderBy(v => v.FourSellerSkuId is > 0 ? 0 : 1)
                     .ThenBy(v => v.SortOrder))
        {
            var combo = ResolveComboKey(row.Title, row.VariantAttributes);
            if (combo is not null && !keptCombos.Add(combo))
            {
                continue;
            }

            kept.Add(row);
        }

        variations.Clear();
        variations.AddRange(kept);
    }

    private static void FillAndUniqueSellerSkus(List<ListingVariationDto> variations, string prefix)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in variations.OrderBy(v => v.FourSellerSkuId is > 0 ? 0 : 1).ThenBy(v => v.SortOrder))
        {
            var sku = row.SellerSku?.Trim();
            if (!string.IsNullOrWhiteSpace(sku) && used.Add(sku))
            {
                continue;
            }

            var previous = sku;
            sku = GenerateUniqueSkuDto(row, used, prefix);
            row.SellerSku = sku;
            if (row.FourSellerSkuId is > 0 &&
                !string.IsNullOrWhiteSpace(previous) &&
                !string.Equals(previous, sku, StringComparison.OrdinalIgnoreCase))
            {
                row.FourSellerSkuId = null;
            }
        }
    }

    private static bool HasUniqueEntitySkus(IEnumerable<ListingVariation> variations)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in variations)
        {
            var sku = row.SellerSku?.Trim();
            if (string.IsNullOrWhiteSpace(sku) || !seen.Add(sku))
            {
                return false;
            }
        }

        return true;
    }

    internal static string? ResolveComboKey(string? title, string? variantAttributes)
    {
        var attrs = ParseAttrs(variantAttributes);
        if (attrs.Count == 0 && !string.IsNullOrWhiteSpace(title))
        {
            attrs = ParseAttrs(BuildVariantAttributesJsonFromTitle(title));
        }

        if (attrs.Count == 0)
        {
            return null;
        }

        var parts = ListingSalePropSync.AxisOrder
            .Select(axis => (Axis: axis, Value: attrs.FirstOrDefault(kv =>
                axis.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)).Value))
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Axis}={p.Value!.Trim().ToLowerInvariant()}")
            .ToList();

        return parts.Count == 0 ? null : string.Join('|', parts);
    }

  internal static bool ShouldClearIncomingSellerSku(
        string? incomingSku,
        IReadOnlySet<string> usedSellerSkus)
    {
        var sku = incomingSku?.Trim();
        return !string.IsNullOrWhiteSpace(sku) && usedSellerSkus.Contains(sku);
    }

    private static string GenerateUniqueSkuDto(
        ListingVariationDto row,
        HashSet<string> used,
        string prefix)
    {
        string sku;
        var attempts = 0;
        do
        {
            var attrPart = BuildVariantAttributeSku(row);
            sku = $"{prefix}-{attrPart}-{GenerateRandomAlphanumeric(6)}";
            attempts++;
        } while (used.Contains(sku) && attempts < 20);

        if (used.Contains(sku))
        {
            throw new Domain.Exceptions.BusinessException("Không tạo được SKU unique cho variant.");
        }

        used.Add(sku);
        return sku;
    }

    private static string GenerateUniqueSku(
        ListingVariation row,
        HashSet<string> used,
        string? prefix)
    {
        prefix ??= NormalizeNumericSkuPrefix(null);
        string sku;
        var attempts = 0;
        do
        {
            var attrPart = BuildVariantAttributeSku(row);
            sku = $"{prefix}-{attrPart}-{GenerateRandomAlphanumeric(6)}";
            attempts++;
        } while (used.Contains(sku) && attempts < 20);

        if (used.Contains(sku))
        {
            throw new Domain.Exceptions.BusinessException("Không tạo được SKU unique cho variant.");
        }

        used.Add(sku);
        return sku;
    }

    private static string BuildVariantAttributeSku(ListingVariationDto row)
    {
        if (!string.IsNullOrWhiteSpace(row.VariantAttributes))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.VariantAttributes);
                var parts = new List<string>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(prop.Value.GetString()?.Replace(" ", "", StringComparison.Ordinal) ?? "");
                    }
                }

                if (parts.Count > 0)
                {
                    return string.Join("-", parts);
                }
            }
            catch (JsonException)
            {
                // ignore
            }
        }

        return "Default";
    }

    private static string BuildVariantAttributeSku(ListingVariation row) =>
        BuildVariantAttributeSku(new ListingVariationDto
        {
            VariantAttributes = row.VariantAttributes,
            Title = row.Title
        });

    private static string NormalizeNumericSkuPrefix(string? externalProductId)
    {
        var digits = new string((externalProductId ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length >= 9)
        {
            return digits[..9];
        }

        if (digits.Length > 0)
        {
            return digits.PadLeft(9, '0');
        }

        return Random.Shared.Next(100000000, 999999999).ToString(CultureInfo.InvariantCulture);
    }

    private static string GenerateRandomAlphanumeric(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = SkuAlphanum[Random.Shared.Next(SkuAlphanum.Length)];
        }

        return new string(chars);
    }

    private static Dictionary<string, string> ParseAttrs(string? attrsJson)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(attrsJson))
        {
            return dict;
        }

        try
        {
            using var doc = JsonDocument.Parse(attrsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return dict;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var value = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        dict[prop.Name] = value.Trim();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return dict;
    }

    private static string? BuildVariantAttributesJsonFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var parts = title.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Color"] = parts[0],
            ["Size"] = parts[1]
        };
        if (parts.Length >= 3)
        {
            dict["Number of Items"] = parts[2];
        }

        return JsonSerializer.Serialize(dict);
    }
}
