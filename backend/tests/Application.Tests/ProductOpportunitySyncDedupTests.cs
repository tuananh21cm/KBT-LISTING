using Application.Services;
using Xunit;

namespace Application.Tests;

public class ProductOpportunitySyncDedupTests
{
    private static readonly Guid KeywordA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid KeywordB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid KeywordC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void DedupLeadIds_KeepsFirst_SkipsEmptyAndDuplicates()
    {
        var ids = ProductOpportunitySyncApply.DedupLeadIds([" lead-1 ", "lead-2", "", "lead-1", "  ", null, "lead-3"]);
        Assert.Equal(["lead-1", "lead-2", "lead-3"], ids);
    }

    [Fact]
    public void SplitInsertUpdate_SecondShopSameLeadId_DoesNotInsert()
    {
        var payload = ProductOpportunitySyncApply.DedupLeadIds(["lead-1", "lead-2"]);
        var existing = new HashSet<string>(StringComparer.Ordinal) { "lead-1" };

        var (insert, update) = ProductOpportunitySyncApply.SplitInsertUpdate(payload, existing);

        Assert.Equal(["lead-2"], insert);
        Assert.Equal(["lead-1"], update);
    }

    [Fact]
    public void SplitInsertUpdate_FirstShop_InsertsAll()
    {
        var payload = ProductOpportunitySyncApply.DedupLeadIds(["lead-1", "lead-2"]);
        var (insert, update) = ProductOpportunitySyncApply.SplitInsertUpdate(
            payload,
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(["lead-1", "lead-2"], insert);
        Assert.Empty(update);
    }

    [Fact]
    public void DiffLinks_SecondShop_AddsLinkWithoutDuplicatingKeyword()
    {
        var existingForShop2 = Array.Empty<Guid>();
        var desired = new[] { KeywordA };

        var diff = ProductOpportunitySyncApply.DiffLinks(existingForShop2, desired);

        Assert.Equal([KeywordA], diff.ToAdd);
        Assert.Empty(diff.ToRemove);
    }

    [Fact]
    public void DiffLinks_ResyncSameShop_DoesNotDuplicateLink()
    {
        var existing = new[] { KeywordA, KeywordB };
        var desired = new[] { KeywordA, KeywordB };

        var diff = ProductOpportunitySyncApply.DiffLinks(existing, desired);

        Assert.Empty(diff.ToAdd);
        Assert.Empty(diff.ToRemove);
    }

    [Fact]
    public void DiffLinks_LeadMissingFromPayload_UnlinksThisShopOnly()
    {
        var existing = new[] { KeywordA, KeywordB };
        var desired = new[] { KeywordA };

        var diff = ProductOpportunitySyncApply.DiffLinks(existing, desired);

        Assert.Empty(diff.ToAdd);
        Assert.Equal([KeywordB], diff.ToRemove);
    }

    [Fact]
    public void FilterUnlinkPreservingSaved_KeepsBookmarkedKeyword()
    {
        var toRemove = new List<Guid> { KeywordA, KeywordB };
        var opportunities = new Dictionary<Guid, (string LeadId, string LeadName)>
        {
            [KeywordA] = ("lead-a", "Plus Size Dress"),
            [KeywordB] = ("lead-b", "Summer Top"),
        };
        var savedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            ProductOpportunitySavedApply.Normalize("Plus Size Dress"),
        };

        var filtered = ProductOpportunitySyncApply.FilterUnlinkPreservingSaved(
            toRemove,
            opportunities,
            savedKeys,
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal([KeywordB], filtered);
    }

    [Fact]
    public void FilterUnlinkPreservingSaved_MatchesSavedLeadId()
    {
        var toRemove = new List<Guid> { KeywordA };
        var opportunities = new Dictionary<Guid, (string LeadId, string LeadName)>
        {
            [KeywordA] = ("lead-saved", "Renamed Keyword"),
        };

        var filtered = ProductOpportunitySyncApply.FilterUnlinkPreservingSaved(
            toRemove,
            opportunities,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "lead-saved" });

        Assert.Empty(filtered);
    }

    [Fact]
    public void OrphansAfterUnlink_KeepsKeywordWhenOtherShopStillLinked()
    {
        var unlinked = new[] { KeywordB };
        var stillLinked = new HashSet<Guid> { KeywordB };

        var orphans = ProductOpportunitySyncApply.OrphansAfterUnlink(unlinked, stillLinked);

        Assert.Empty(orphans);
    }

    [Fact]
    public void OrphansAfterUnlink_PrunesWhenZeroLinksRemain()
    {
        var unlinked = new[] { KeywordB, KeywordC };
        var stillLinked = new HashSet<Guid> { KeywordC };

        var orphans = ProductOpportunitySyncApply.OrphansAfterUnlink(unlinked, stillLinked);

        Assert.Equal([KeywordB], orphans);
    }

    [Fact]
    public void FullSyncPlan_TwoShopsShareLeadId_OneInsertTwoLinks()
    {
        var payload = ProductOpportunitySyncApply.DedupLeadIds(["lead-1", "lead-1"]);
        Assert.Equal(["lead-1"], payload);

        var afterShop1 = new HashSet<string>(StringComparer.Ordinal);
        var (insert1, _) = ProductOpportunitySyncApply.SplitInsertUpdate(payload, afterShop1);
        Assert.Equal(["lead-1"], insert1);

        afterShop1.Add("lead-1");
        var (insert2, update2) = ProductOpportunitySyncApply.SplitInsertUpdate(payload, afterShop1);
        Assert.Empty(insert2);
        Assert.Equal(["lead-1"], update2);

        var shop1Links = ProductOpportunitySyncApply.DiffLinks([], [KeywordA]);
        var shop2Links = ProductOpportunitySyncApply.DiffLinks([], [KeywordA]);
        Assert.Equal([KeywordA], shop1Links.ToAdd);
        Assert.Equal([KeywordA], shop2Links.ToAdd);

        var shop1Resync = ProductOpportunitySyncApply.DiffLinks([KeywordA], [KeywordA]);
        Assert.Empty(shop1Resync.ToAdd);
        Assert.Empty(shop1Resync.ToRemove);
    }
}
