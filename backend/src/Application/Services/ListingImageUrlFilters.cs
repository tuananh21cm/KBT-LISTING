using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.DTOs.Listing;

namespace Application.Services;

/// <summary>Filters unsupported media (e.g. .gif images, supplier product video) before mirror persist and 4Seller push.</summary>
public static class ListingImageUrlFilters
{
    private static readonly Regex GifImgTagRegex = new(
        @"<img\b[^>]*\bsrc\s*=\s*[""']([^""']+\.gif(?:\?[^""']*)?)[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>True when the URL path ends with <c>.gif</c> (query string ignored).</summary>
    public static bool IsGifImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.AbsolutePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
        }

        var path = trimmed.Split('?', '#')[0];
        return path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Removes GIF URLs from a pipe-joined image field.</summary>
    public static string? StripGifFromPipeJoined(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static u => !IsGifImageUrl(u))
            .ToList();
        return parts.Count == 0 ? null : string.Join('|', parts);
    }

    /// <summary>Strips GIF URLs from push payload media fields.</summary>
    public static void StripGifUrlsFromPayload(JsonObject obj)
    {
        if (obj["mainImage"] is JsonValue mainJv &&
            mainJv.TryGetValue(out string? mainRaw) &&
            !string.IsNullOrWhiteSpace(mainRaw))
        {
            var cleaned = StripGifFromPipeJoined(mainRaw);
            obj["mainImage"] = cleaned ?? string.Empty;
        }

        StripGifsFromImageListNode(obj["imageList"]);

        CleanupSkuImgsNode(obj["skuImgs"]);
        if (obj["salePropImageMap"] is JsonObject saleMap)
        {
            CleanupSkuImgsNode(saleMap);
            obj["salePropImageMap"] = saleMap.DeepClone();
        }

        foreach (var variationKey in new[] { "variation", "variationList", "variations", "skuList", "skus" })
        {
            if (obj[variationKey] is not JsonArray varArr)
            {
                continue;
            }

            foreach (var item in varArr)
            {
                if (item is not JsonObject vObj)
                {
                    continue;
                }

                if (vObj["mainImage"] is JsonValue vMain &&
                    vMain.TryGetValue(out string? vMainRaw) &&
                    !string.IsNullOrWhiteSpace(vMainRaw))
                {
                    var cleaned = StripGifFromPipeJoined(vMainRaw) ?? StripFirstNonGif(vMainRaw);
                    vObj["mainImage"] = cleaned ?? string.Empty;
                }
            }
        }

        foreach (var key in new[] { "descriptionHtml", "description" })
        {
            if (obj[key] is JsonValue descJv &&
                descJv.TryGetValue(out string? html) &&
                !string.IsNullOrWhiteSpace(html))
            {
                obj[key] = StripGifImgTagsFromHtml(html);
            }
        }
    }

    /// <summary>True when gallery has a Product video row or <c>erpVideoId</c> is set.</summary>
    public static bool HasProductVideo(ListingDetailDto detail) =>
        !string.IsNullOrWhiteSpace(detail.ErpVideoId)
        || (detail.Images ?? []).Any(IsProductVideoRow);

    /// <summary>Removes Product video from in-memory listing detail (auto publish prep).</summary>
    public static void RemoveProductVideo(ListingDetailDto detail)
    {
        detail.ErpVideoId = null;
        if (detail.Images is null || detail.Images.Count == 0)
        {
            return;
        }

        detail.Images.RemoveAll(IsProductVideoRow);
    }

    /// <summary>Clears Product video keys from push RawPayload.</summary>
    public static void StripProductVideoFromPayload(JsonObject obj)
    {
        foreach (var key in ProductVideoPayloadKeys)
        {
            obj[key] = string.Empty;
        }
    }

    private static readonly string[] ProductVideoPayloadKeys =
    [
        "erpVideoUrl",
        "videoUrl",
        "productVideo",
        "videoPostUrl",
        "erpVideoId",
        "videoId"
    ];

    private static bool IsProductVideoRow(ListingImageDto img) =>
        string.Equals(img.MediaType, "Video", StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes GIF URLs from skuImgs / salePropImageMap nodes in RawPayload.</summary>
    public static void CleanupSkuImgsNode(JsonNode? node)
    {
        if (node is JsonObject map)
        {
            foreach (var key in map.Select(static kv => kv.Key).ToList())
            {
                var cleaned = FilterUrlListNode(map[key]);
                if (cleaned is null)
                {
                    map.Remove(key);
                }
                else
                {
                    map[key] = cleaned;
                }
            }

            return;
        }

        if (node is JsonValue jv &&
            jv.TryGetValue(out string? raw) &&
            !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                if (JsonNode.Parse(raw) is not JsonArray rows)
                {
                    return;
                }

                var next = new JsonArray();
                foreach (var item in rows)
                {
                    if (item is not JsonObject row)
                    {
                        continue;
                    }

                    var imgUrl = GetRowImageUrl(row);
                    if (string.IsNullOrWhiteSpace(imgUrl))
                    {
                        continue;
                    }

                    var cleaned = StripGifFromPipeJoined(imgUrl);
                    if (string.IsNullOrWhiteSpace(cleaned))
                    {
                        continue;
                    }

                    row["img_url"] = cleaned;
                    row["imgUrl"] = cleaned;
                    next.Add(row.DeepClone()!);
                }

                node.ReplaceWith(JsonValue.Create(next.ToJsonString()));
            }
            catch (JsonException)
            {
                // leave malformed skuImgs unchanged
            }
        }
    }

    private static void StripGifsFromImageListNode(JsonNode? node)
    {
        if (node is JsonArray arr)
        {
            if (arr.Count == 0 || arr[0] is JsonValue)
            {
                var kept = new JsonArray();
                foreach (var item in arr)
                {
                    switch (item)
                    {
                        case JsonValue jv when jv.TryGetValue(out string? s) && !IsGifImageUrl(s):
                            kept.Add(JsonValue.Create(s)!);
                            break;
                        case JsonObject jo:
                            var url = GetStringFromObject(jo, "url", "src", "imageUrl", "img_url", "imgUrl");
                            if (!string.IsNullOrWhiteSpace(url) && !IsGifImageUrl(url))
                            {
                                kept.Add(jo.DeepClone()!);
                            }

                            break;
                    }
                }

                node.ReplaceWith(kept);
            }

            return;
        }

        if (node is JsonValue listJv &&
            listJv.TryGetValue(out string? rawList) &&
            !string.IsNullOrWhiteSpace(rawList))
        {
            var cleaned = StripGifFromPipeJoined(rawList);
            node.ReplaceWith(JsonValue.Create(cleaned ?? string.Empty));
        }
    }

    private static JsonNode? FilterUrlListNode(JsonNode? node)
    {
        if (node is JsonArray arr)
        {
            var kept = arr
                .Select(static item => item?.GetValue<string>()?.Trim())
                .Where(static u => !string.IsNullOrWhiteSpace(u) && !IsGifImageUrl(u))
                .Select(static u => (JsonNode?)JsonValue.Create(u!))
                .ToArray();
            return kept.Length == 0 ? null : new JsonArray(kept);
        }

        if (node is JsonValue jv && jv.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
        {
            var cleaned = StripGifFromPipeJoined(s);
            return string.IsNullOrWhiteSpace(cleaned) ? null : JsonValue.Create(cleaned);
        }

        return node;
    }

    private static string? StripFirstNonGif(string raw)
    {
        return raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static u => !IsGifImageUrl(u));
    }

    private static string? GetRowImageUrl(JsonObject row) =>
        GetStringFromObject(row, "img_url", "imgUrl", "url");

    private static string? GetStringFromObject(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj[key] is JsonValue jv && jv.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
            {
                return s.Trim();
            }
        }

        return null;
    }

    private static string StripGifImgTagsFromHtml(string html) =>
        GifImgTagRegex.Replace(html, string.Empty);
}
