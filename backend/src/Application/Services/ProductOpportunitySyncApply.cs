namespace Application.Services;

/// <summary>
/// Pure set-diff for keyword sync: insert vs skip by lead_id, shop link replace, orphan prune.
/// </summary>
public static class ProductOpportunitySyncApply
{
    /// <summary>First-seen trimmed lead ids; empty and later duplicates dropped.</summary>
    public static List<string> DedupLeadIds(IEnumerable<string?> leadIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var raw in leadIds)
        {
            var id = (raw ?? string.Empty).Trim();
            if (id.Length == 0 || !seen.Add(id))
            {
                continue;
            }

            ordered.Add(id);
        }

        return ordered;
    }

    /// <summary>Payload lead ids that need a new keyword row vs those that already exist.</summary>
    public static (List<string> Insert, List<string> Update) SplitInsertUpdate(
        IReadOnlyList<string> payloadLeadIds,
        IReadOnlySet<string> existingLeadIds)
    {
        var insert = new List<string>();
        var update = new List<string>();
        foreach (var id in payloadLeadIds)
        {
            if (existingLeadIds.Contains(id))
            {
                update.Add(id);
            }
            else
            {
                insert.Add(id);
            }
        }

        return (insert, update);
    }

    /// <summary>Shop-link rows to add or drop so this shop matches the payload set.</summary>
    public static LinkDiff DiffLinks(
        IReadOnlyCollection<Guid> existingOpportunityIdsForShop,
        IReadOnlyCollection<Guid> desiredOpportunityIds)
    {
        var existing = existingOpportunityIdsForShop as HashSet<Guid>
            ?? existingOpportunityIdsForShop.ToHashSet();
        var desired = desiredOpportunityIds as HashSet<Guid>
            ?? desiredOpportunityIds.ToHashSet();
        var toAdd = desired.Where(id => !existing.Contains(id)).ToList();
        var toRemove = existing.Where(id => !desired.Contains(id)).ToList();
        return new LinkDiff(toAdd, toRemove);
    }

    /// <summary>Keyword ids that lost this shop's link and have no remaining shop link.</summary>
    public static List<Guid> OrphansAfterUnlink(
        IReadOnlyCollection<Guid> unlinkedOpportunityIds,
        IReadOnlySet<Guid> stillLinkedOpportunityIds)
    {
        return unlinkedOpportunityIds
            .Where(id => !stillLinkedOpportunityIds.Contains(id))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// True when a shop link must survive replace-per-shop sync because the user bookmarked
    /// this keyword on that shop (match normalized <c>lead_name</c> or saved <c>lead_id</c>).
    /// </summary>
    public static bool ShouldPreserveLinkOnSync(
        string? leadId,
        string? leadName,
        IReadOnlySet<string> savedNormalizedKeywords,
        IReadOnlySet<string> savedLeadIds)
    {
        var id = (leadId ?? string.Empty).Trim();
        if (id.Length > 0 && savedLeadIds.Contains(id))
        {
            return true;
        }

        var key = ProductOpportunitySavedApply.Normalize(leadName);
        return key.Length > 0 && savedNormalizedKeywords.Contains(key);
    }

    /// <summary>
    /// Drop unlink candidates that are bookmarked on this shop — saved keywords stay linked
    /// even when TikTok no longer returns them in the latest scrape.
    /// </summary>
    public static List<Guid> FilterUnlinkPreservingSaved(
        IReadOnlyList<Guid> toRemove,
        IReadOnlyDictionary<Guid, (string LeadId, string LeadName)> opportunityById,
        IReadOnlySet<string> savedNormalizedKeywords,
        IReadOnlySet<string> savedLeadIds) =>
        toRemove
            .Where(id =>
            {
                if (!opportunityById.TryGetValue(id, out var opp))
                {
                    return true;
                }

                return !ShouldPreserveLinkOnSync(
                    opp.LeadId,
                    opp.LeadName,
                    savedNormalizedKeywords,
                    savedLeadIds);
            })
            .ToList();

    /// <summary>Link add/remove sets for one shop.</summary>
    public sealed record LinkDiff(IReadOnlyList<Guid> ToAdd, IReadOnlyList<Guid> ToRemove);
}
