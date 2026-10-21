using System.Text;

namespace Application.Services;

/// <summary>
/// Parse / filter AI-suggested TikTok search keywords when a shop has no usable
/// on-niche snapshot (empty, all-generic, or all off-audience).
/// </summary>
public static class ShopKeywordResearchSuggest
{
    /// <summary>Research grounded on the TikTok keyword snapshot.</summary>
    public const string SourceSnapshot = "snapshot";

    /// <summary>Research invented from catalog audience (empty or unusable snapshot).</summary>
    public const string SourceAudienceSuggest = "audienceSuggest";

    /// <summary>Cap for list-now when inventing keywords (no volume to rank).</summary>
    public const int MaxListNow = 8;

    /// <summary>Upcoming horizon size (same as snapshot path).</summary>
    public const int UpcomingCount = 5;

    /// <summary>Extra keywords the model should return so generic drops still leave a full list.</summary>
    public const int ExtraBuffer = 4;

    /// <summary>Minimum tokens in a usable search phrase.</summary>
    public const int MinWords = 2;

    /// <summary>Maximum tokens — longer strings are titles, not search keywords.</summary>
    public const int MaxWords = 8;

    /// <summary>Shown when the shop has zero ProductOpportunities rows.</summary>
    public const string EmptySnapshotWarning =
        "Chưa có snapshot Trending TikTok — đề xuất theo tệp khách. Đồng bộ extension để có volume thật.";

    /// <summary>Shown when every snapshot keyword was dropped as generic.</summary>
    public const string GenericOnlySnapshotWarning =
        "Snapshot chỉ còn keyword generic (outfit, maxi dress, …) — đề xuất theo tệp khách. Đồng bộ lại trending để có cụm người dùng tìm kiếm.";

    /// <summary>Shown when scored snapshot keywords all fail the audience gate.</summary>
    public const string OffNicheSnapshotWarning =
        "Snapshot không còn keyword khớp ngách sau khi lọc generic và lệch tệp — đề xuất theo tệp khách. Đồng bộ lại trending hoặc nới danh mục / style nếu ngách đang quá hẹp.";

    /// <summary>Vietnamese fallback when the model skipped rationale.</summary>
    public const string DefaultRationale =
        "Khớp tệp khách (category / tuổi / body / style) hơn keyword generic.";

    /// <summary>One accepted search phrase plus copy.</summary>
    public readonly record struct AcceptedKeyword(
        string Keyword,
        string? Rationale,
        string? Fit,
        IReadOnlyList<string>? MustTokens = null);

    /// <summary>Lowercase display form: letters/digits, spaces, hyphen, apostrophe.</summary>
    public static string NormalizeDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var cleaned = raw.Trim().ToLowerInvariant()
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('/', ' ');
        var sb = new StringBuilder(cleaned.Length);
        var prevSpace = false;
        foreach (var ch in cleaned)
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(ch);
                prevSpace = false;
            }
            else if (ch is '\'' or '-')
            {
                if (sb.Length == 0 || prevSpace)
                {
                    continue;
                }

                sb.Append(ch);
                prevSpace = false;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
        }

        return sb.ToString().Trim().Trim('-', '\'');
    }

    /// <summary>
    /// Accept a model keyword when it is a specific English search phrase that is not generic
    /// and does not repeat a live listing.
    /// </summary>
    public static bool TryAccept(
        string? raw,
        IReadOnlyList<string> categoryNames,
        IReadOnlyList<string> listingTitles,
        ISet<string> seen,
        out string keyword,
        string? bodySizeCode = null)
    {
        keyword = NormalizeDisplay(raw);
        if (keyword.Length == 0)
        {
            return false;
        }

        var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < MinWords || words.Length > MaxWords)
        {
            return false;
        }

        if (ShopKeywordResearchScoring.IsGeneric(keyword, categoryNames))
        {
            return false;
        }

        if (!AudienceSearchQuery.HasRequiredBodyModifier(keyword, bodySizeCode))
        {
            return false;
        }

        if (ShopKeywordResearchScoring.OverlapsListing(keyword, listingTitles))
        {
            return false;
        }

        var key = ShopKeywordResearchScoring.Normalize(keyword);
        if (key.Length == 0 || !seen.Add(key))
        {
            return false;
        }

        return true;
    }

    /// <summary>Take up to <paramref name="take"/> distinct accepted keywords, preserving order.</summary>
    public static List<AcceptedKeyword> Take(
        IEnumerable<(string? Keyword, string? Rationale, string? Fit)> source,
        int take,
        IReadOnlyList<string> categoryNames,
        IReadOnlyList<string> listingTitles,
        ISet<string> seen,
        string? bodySizeCode = null)
        => Take(
            source.Select(x => (x.Keyword, x.Rationale, x.Fit, (IReadOnlyList<string>?)null)),
            take,
            categoryNames,
            listingTitles,
            seen,
            bodySizeCode);

    /// <summary>Take up to <paramref name="take"/> distinct accepted keywords, preserving order.</summary>
    public static List<AcceptedKeyword> Take(
        IEnumerable<(string? Keyword, string? Rationale, string? Fit, IReadOnlyList<string>? MustTokens)> source,
        int take,
        IReadOnlyList<string> categoryNames,
        IReadOnlyList<string> listingTitles,
        ISet<string> seen,
        string? bodySizeCode = null)
    {
        var result = new List<AcceptedKeyword>(Math.Max(0, take));
        if (take <= 0)
        {
            return result;
        }

        foreach (var item in source)
        {
            if (!TryAccept(item.Keyword, categoryNames, listingTitles, seen, out var keyword, bodySizeCode))
            {
                continue;
            }

            result.Add(new AcceptedKeyword(
                keyword,
                CleanCopy(item.Rationale),
                CleanCopy(item.Fit),
                item.MustTokens));
            if (result.Count >= take)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Move upcoming keywords into list-now when the now list is still under <paramref name="minNow"/>.
    /// </summary>
    public static void BackfillNowFromUpcoming(
        List<AcceptedKeyword> now,
        List<AcceptedKeyword> upcoming,
        int minNow)
    {
        if (now.Count >= minNow || upcoming.Count == 0)
        {
            return;
        }

        var need = minNow - now.Count;
        var take = Math.Min(need, upcoming.Count);
        now.AddRange(upcoming.Take(take));
        upcoming.RemoveRange(0, take);
    }

    private static string? CleanCopy(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        return t.Length == 0 ? null : ShopKeywordResearchPersistence.StripDuplicateVolumeMetric(t);
    }
}
