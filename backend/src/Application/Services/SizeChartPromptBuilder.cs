using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Application.Services;

/// <summary>
/// Builds Grok Imagine prompts for size-chart infographics from Shein normalized size guide JSON.
/// </summary>
public static class SizeChartPromptBuilder
{
    private const string DefaultMeasureNote =
        "Please allow 0.4-0.8 inch measure error due to manual measurement.";

    private static readonly (string Column, string[] Keywords)[] MeasurementColumns =
    [
        ("BUST", ["bust", "chest", "胸围", "胸"]),
        ("WAIST", ["waist", "腰围", "腰"]),
        ("HIP", ["hip", "hips", "hipp", "臀围", "臀"]),
        ("LENGTH", ["length", "dress length", "top length", "衣长", "裙长", "长度"])
    ];

    private static readonly string[] ProductTypeTokens =
    [
        "Matching Set", "Two Piece Set", "2 Piece Set", "Jumpsuit", "Romper", "Overalls",
        "Sweater", "Cardigan", "Hoodie", "Blazer", "Jacket", "Coat", "Trench Coat",
        "Mini Dress", "Midi Dress", "Maxi Dress", "Dress", "Skirt", "Shorts", "Pants",
        "Jeans", "Leggings", "Bodysuit", "Tank Top", "Camisole", "Blouse", "Shirt",
        "T-Shirt", "Top", "Tunic", "Nightgown", "Pajama Set", "Swimsuit", "Bikini"
    ];

    /// <summary>
    /// Builds the final prompt from template, product title and size guide JSON.
    /// </summary>
    public static string Build(string promptTemplate, JsonElement sizeGuideJson, string? productName)
    {
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            throw new InvalidOperationException("SizeChartPrompt chưa được cấu hình.");
        }

        var productType = ExtractProductType(productName);
        var sizeData = BuildSizeDataBlock(sizeGuideJson);
        var measureGuides = BuildMeasureGuidesBlock(sizeGuideJson);
        var measureNote = GetMeasureNote(sizeGuideJson);

        return promptTemplate
            .Replace("{{PRODUCT_TYPE}}", productType, StringComparison.Ordinal)
            .Replace("{{SIZE_CHART_DATA}}", sizeData, StringComparison.Ordinal)
            .Replace("{{MEASURE_GUIDES}}", measureGuides, StringComparison.Ordinal)
            .Replace("{{MEASURE_NOTE}}", measureNote, StringComparison.Ordinal);
    }

    /// <summary>Heuristic product type noun from listing title.</summary>
    internal static string ExtractProductType(string? productName)
    {
        var title = (productName ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return "Garment";
        }

        var cleaned = Regex.Replace(title, @"\b(SHEIN|ROMWE|Coolane|Muchica)\b", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        foreach (var token in ProductTypeTokens)
        {
            if (cleaned.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return token;
            }
        }

        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2 &&
            words[^1].Equals("Set", StringComparison.OrdinalIgnoreCase))
        {
            return $"{words[^2]} Set";
        }

        return words.Length > 0 ? words[^1] : "Garment";
    }

    /// <summary>Formats SIZE | US | BUST | WAIST | HIP | LENGTH table (inch).</summary>
    internal static string BuildSizeDataBlock(JsonElement sizeGuideJson)
    {
        if (sizeGuideJson.TryGetProperty("chartRows", out var chartRowsEl) &&
            chartRowsEl.ValueKind == JsonValueKind.Array &&
            chartRowsEl.GetArrayLength() > 0)
        {
            return BuildSizeDataFromChartRows(chartRowsEl);
        }

        if (!sizeGuideJson.TryGetProperty("rows", out var rowsEl) ||
            rowsEl.ValueKind != JsonValueKind.Array ||
            rowsEl.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("SizeGuideJson thiếu chartRows hoặc rows.");
        }

        var unit = GetUnit(sizeGuideJson);
        var dimensionMap = BuildDimensionMap(sizeGuideJson);
        var bodyHipMap = BuildBodyHipMap(sizeGuideJson);
        var sb = new StringBuilder();
        sb.AppendLine("SIZE | US | BUST | WAIST | HIP | LENGTH");

        foreach (var rowEl in rowsEl.EnumerateArray())
        {
            if (rowEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var size = GetString(rowEl, "size") ?? "—";
            var usSize = GetString(rowEl, "usSize") ?? "—";

            if (!rowEl.TryGetProperty("measurements", out var measurements) ||
                measurements.ValueKind != JsonValueKind.Object)
            {
                measurements = default;
            }

            var bust = FormatLegacyMeasurement(measurements, dimensionMap, "BUST", unit);
            var waist = FormatLegacyMeasurement(measurements, dimensionMap, "WAIST", unit);
            var hip = FormatLegacyMeasurement(measurements, dimensionMap, "HIP", unit);
            if (hip == "—" && bodyHipMap.TryGetValue(size, out var bodyHip))
            {
                hip = bodyHip;
            }

            var length = FormatLegacyMeasurement(measurements, dimensionMap, "LENGTH", unit);

            sb.Append(CultureInfo.InvariantCulture, $"{size} | {usSize} | {bust} | {waist} | {hip} | {length}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    internal static string BuildMeasureGuidesBlock(JsonElement sizeGuideJson)
    {
        var product = ParseMeasureHints(sizeGuideJson, "productMeasureGuide");
        var body = ParseMeasureHints(sizeGuideJson, "bodyMeasureGuide");

        string? FindHint(string column, List<(string Name, string Description)> hints)
        {
            foreach (var (name, description) in hints)
            {
                if (MapColumn(name) == column && !string.IsNullOrWhiteSpace(description))
                {
                    return description.Trim();
                }
            }

            return null;
        }

        var bust = FindHint("BUST", product) ?? FindHint("BUST", body);
        var waist = FindHint("WAIST", product) ?? FindHint("WAIST", body);
        var hip = FindHint("HIP", product) ?? FindHint("HIP", body);
        var length = FindHint("LENGTH", product) ?? FindHint("LENGTH", body);

        var sb = new StringBuilder();
        AppendGuideLine(sb, "BUST", bust);
        AppendGuideLine(sb, "WAIST", waist);
        AppendGuideLine(sb, "HIP", hip);
        AppendGuideLine(sb, "LENGTH", length);
        return sb.ToString().TrimEnd();
    }

    internal static string GetMeasureNote(JsonElement sizeGuideJson)
    {
        var note = GetString(sizeGuideJson, "measureNote");
        return string.IsNullOrWhiteSpace(note) ? DefaultMeasureNote : note.Trim();
    }

    /// <summary>Converts cm string to inch (1 decimal). Handles single values and ranges.</summary>
    internal static string CmToInch(string? cmValue)
    {
        if (string.IsNullOrWhiteSpace(cmValue) || cmValue.Trim() == "—")
        {
            return "—";
        }

        var trimmed = StripUnitSuffix(cmValue.Trim());
        if (trimmed.Contains('-', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                var lo = ConvertSingleCmToInch(parts[0]);
                var hi = ConvertSingleCmToInch(parts[1]);
                if (lo != "—" && hi != "—")
                {
                    return $"{lo}-{hi}";
                }
            }
        }

        if (trimmed.Contains('/', StringComparison.Ordinal))
        {
            var segments = trimmed.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var nums = new List<double>();
            foreach (var segment in segments)
            {
                if (TryParseCmNumber(segment, out var cm))
                {
                    nums.Add(Math.Round(cm / 2.54, 1, MidpointRounding.AwayFromZero));
                }
            }

            if (nums.Count >= 2)
            {
                return $"{nums.Min().ToString("0.0", CultureInfo.InvariantCulture)}-{nums.Max().ToString("0.0", CultureInfo.InvariantCulture)}";
            }
        }

        return ConvertSingleCmToInch(trimmed);
    }

    private static string BuildSizeDataFromChartRows(JsonElement chartRowsEl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SIZE | US | BUST | WAIST | HIP | LENGTH");

        foreach (var rowEl in chartRowsEl.EnumerateArray())
        {
            if (rowEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var size = GetString(rowEl, "size") ?? "—";
            var usSize = GetString(rowEl, "usSize") ?? "—";
            var bust = DisplayValue(GetString(rowEl, "bust"));
            var waist = DisplayValue(GetString(rowEl, "waist"));
            var hip = DisplayValue(GetString(rowEl, "hip"));
            var length = DisplayValue(GetString(rowEl, "length"));

            sb.Append(CultureInfo.InvariantCulture, $"{size} | {usSize} | {bust} | {waist} | {hip} | {length}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static List<(string Name, string Description)> ParseMeasureHints(
        JsonElement sizeGuideJson,
        string property)
    {
        var outList = new List<(string Name, string Description)>();
        if (!sizeGuideJson.TryGetProperty(property, out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
        {
            return outList;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = GetString(item, "name") ?? string.Empty;
            var description = GetString(item, "description") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            outList.Add((name, description));
        }

        return outList;
    }

    private static void AppendGuideLine(StringBuilder sb, string label, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        sb.Append(CultureInfo.InvariantCulture, $"{label}: {text.Trim()}");
        sb.AppendLine();
    }

    private static string GetUnit(JsonElement sizeGuideJson)
    {
        var unit = GetString(sizeGuideJson, "unit");
        return string.Equals(unit, "inch", StringComparison.OrdinalIgnoreCase) ? "inch" : "cm";
    }

    private static Dictionary<string, string> BuildBodyHipMap(JsonElement sizeGuideJson)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Legacy cm rows may still benefit from body hip if stored in future extension versions.
        return map;
    }

    private static Dictionary<string, string> BuildDimensionMap(JsonElement sizeGuideJson)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!sizeGuideJson.TryGetProperty("dimensions", out var dims) ||
            dims.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (var dim in dims.EnumerateArray())
        {
            if (dim.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = GetString(dim, "name") ?? GetString(dim, "nameEn");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var column = MapColumn(name);
            if (column is not null && !map.ContainsKey(column))
            {
                map[column] = name.Trim();
            }
        }

        return map;
    }

    private static string? MapColumn(string dimensionName)
    {
        var normalized = dimensionName.Trim().ToLowerInvariant();
        if (normalized.Contains("sleeve", StringComparison.Ordinal) ||
            normalized.Contains("shoulder", StringComparison.Ordinal) ||
            normalized.Contains("inseam", StringComparison.Ordinal) ||
            normalized.Contains("armhole", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var (column, keywords) in MeasurementColumns)
        {
            foreach (var keyword in keywords)
            {
                if (normalized.Contains(keyword, StringComparison.Ordinal))
                {
                    return column;
                }
            }
        }

        return null;
    }

    private static string FormatLegacyMeasurement(
        JsonElement measurements,
        IReadOnlyDictionary<string, string> dimensionMap,
        string column,
        string unit)
    {
        if (measurements.ValueKind != JsonValueKind.Object)
        {
            return "—";
        }

        string? raw = null;
        if (dimensionMap.TryGetValue(column, out var dimName) &&
            measurements.TryGetProperty(dimName, out var direct) &&
            direct.ValueKind == JsonValueKind.String)
        {
            raw = direct.GetString();
        }
        else
        {
            foreach (var prop in measurements.EnumerateObject())
            {
                if (MapColumn(prop.Name) == column && prop.Value.ValueKind == JsonValueKind.String)
                {
                    raw = prop.Value.GetString();
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return "—";
        }

        if (string.Equals(unit, "inch", StringComparison.OrdinalIgnoreCase))
        {
            return column == "LENGTH"
                ? NormalizeInchLength(raw)
                : NormalizeInchValue(raw);
        }

        return column == "LENGTH" ? CmToInch(raw) : CmToInch(raw);
    }

    private static string NormalizeInchValue(string raw)
    {
        var trimmed = StripUnitSuffix(raw.Trim());
        return string.IsNullOrWhiteSpace(trimmed) ? "—" : trimmed;
    }

    private static string NormalizeInchLength(string raw)
    {
        var trimmed = StripUnitSuffix(raw.Trim());
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "—";
        }

        if (!trimmed.Contains('/', StringComparison.Ordinal))
        {
            return trimmed;
        }

        var segments = trimmed.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var nums = new List<double>();
        foreach (var segment in segments)
        {
            if (double.TryParse(segment, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ||
                double.TryParse(segment, NumberStyles.Float, CultureInfo.CurrentCulture, out n))
            {
                nums.Add(n);
            }
        }

        if (nums.Count >= 2)
        {
            return $"{nums.Min().ToString("0.0", CultureInfo.InvariantCulture)}-{nums.Max().ToString("0.0", CultureInfo.InvariantCulture)}";
        }

        return trimmed;
    }

    private static string ConvertSingleCmToInch(string value)
    {
        if (!TryParseCmNumber(value, out var cm))
        {
            return value;
        }

        var inch = Math.Round(cm / 2.54, 1, MidpointRounding.AwayFromZero);
        return inch.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static bool TryParseCmNumber(string value, out double cm)
    {
        var trimmed = StripUnitSuffix(value.Trim());
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out cm) ||
               double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out cm);
    }

    private static string StripUnitSuffix(string value) =>
        Regex.Replace(value, @"\s*(cm|in|inch|inches)\b", "", RegexOptions.IgnoreCase).Trim();

    private static string DisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string? GetString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number when el.TryGetDouble(out var n) =>
                n.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.Null => null,
            _ => null
        };
    }
}
