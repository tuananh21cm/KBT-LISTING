using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingInFlightReconcileTests
{
    [Fact]
    public void ChunkListingIds_SplitsIntoBatchesOfSize()
    {
        var ids = Enumerable.Range(1, 120).Select(i => i.ToString()).ToList();
        var chunks = ListingSyncService.ChunkListingIds(ids, 50).ToList();

        Assert.Equal(3, chunks.Count);
        Assert.Equal(50, chunks[0].Count);
        Assert.Equal(50, chunks[1].Count);
        Assert.Equal(20, chunks[2].Count);
        Assert.Equal("1", chunks[0][0]);
        Assert.Equal("120", chunks[2][^1]);
    }

    [Fact]
    public void ChunkListingIds_EmptySource_YieldsNothing()
    {
        Assert.Empty(ListingSyncService.ChunkListingIds([], 50));
    }

    [Fact]
    public void MissingListingIds_ReturnsAskedNotFound()
    {
        var asked = new[] { "1", "2", "3", "2" };
        var found = new HashSet<string>(StringComparer.Ordinal) { "2" };

        var missing = ListingSyncService.MissingListingIds(asked, found);

        Assert.Equal(new[] { "1", "3" }, missing);
    }

    [Fact]
    public void MayReconcileInFlight_NonInFlight_AlwaysTrue()
    {
        var now = DateTime.UtcNow;
        Assert.True(ListingSyncService.MayReconcileInFlight(PublishStatuses.Draft, now, now));
        Assert.True(ListingSyncService.MayReconcileInFlight(null, null, now));
    }

    [Fact]
    public void MayReconcileInFlight_WithinGrace_False()
    {
        var now = DateTime.UtcNow;
        Assert.False(
            ListingSyncService.MayReconcileInFlight(
                PublishStatuses.UnderReview,
                now.AddMinutes(-10),
                now));
    }

    [Fact]
    public void MayReconcileInFlight_PastGrace_True()
    {
        var now = DateTime.UtcNow;
        Assert.True(
            ListingSyncService.MayReconcileInFlight(
                PublishStatuses.Publishing,
                now.AddMinutes(-45),
                now));
    }

    [Fact]
    public void MayPurgeGoneListing_WithinGrace_False()
    {
        var now = DateTime.UtcNow;
        Assert.False(ListingSyncService.MayPurgeGoneListing(now.AddMinutes(-5), now));
    }

    [Fact]
    public void MayPurgeGoneListing_NoPushOrPastGrace_True()
    {
        var now = DateTime.UtcNow;
        Assert.True(ListingSyncService.MayPurgeGoneListing(null, now));
        Assert.True(ListingSyncService.MayPurgeGoneListing(now.AddHours(-2), now));
    }
}
