using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingSellerSyncLeaseTests
{
    [Fact]
    public void ExtensionLease_BlocksBackground_ButNotManual()
    {
        var lease = new ListingSellerSyncLease();
        var ownerId = Guid.NewGuid();

        lease.TouchExtensionLease(ownerId, TimeSpan.FromSeconds(60));
        Assert.True(lease.HasActiveExtensionLease(ownerId));

        Assert.False(lease.TryBegin(ownerId, ListingSyncLeaseSource.Background, out var bgHandle));
        Assert.Null(bgHandle);

        Assert.True(lease.TryBegin(ownerId, ListingSyncLeaseSource.Manual, out var manualHandle));
        Assert.NotNull(manualHandle);
        Assert.False(lease.HasActiveExtensionLease(ownerId)); // Manual preempts presence lease

        Assert.False(lease.TryBegin(ownerId, ListingSyncLeaseSource.Extension, out _));

        manualHandle!.Dispose();
        Assert.True(lease.TryBegin(ownerId, ListingSyncLeaseSource.Extension, out var extHandle));
        extHandle!.Dispose();
    }

    [Fact]
    public void ExtensionLeaseTtl_IsAtLeastNinetySeconds()
    {
        Assert.True(PostHtmlDraftSyncScheduler.ExtensionLeaseTtl.TotalSeconds >= 90);
    }

    [Fact]
    public void AutoPublish_AllowsConcurrentSameSeller_BlocksBackgroundSync()
    {
        var lease = new ListingSellerSyncLease();
        var ownerId = Guid.NewGuid();

        Assert.True(lease.TryBegin(ownerId, ListingSyncLeaseSource.AutoPublish, out var first));
        Assert.True(lease.TryBegin(ownerId, ListingSyncLeaseSource.AutoPublish, out var second));
        Assert.NotNull(first);
        Assert.NotNull(second);

        Assert.False(lease.TryBegin(ownerId, ListingSyncLeaseSource.Background, out _));
        Assert.False(lease.TryBegin(ownerId, ListingSyncLeaseSource.Manual, out _));

        first!.Dispose();
        Assert.False(lease.TryBegin(ownerId, ListingSyncLeaseSource.Background, out _));

        second!.Dispose();
        Assert.True(lease.TryBegin(ownerId, ListingSyncLeaseSource.Background, out var bgHandle));
        bgHandle!.Dispose();
    }

    [Fact]
    public void AutoPublish_BlockedByExtensionLease()
    {
        var lease = new ListingSellerSyncLease();
        var ownerId = Guid.NewGuid();

        lease.TouchExtensionLease(ownerId, TimeSpan.FromSeconds(60));

        Assert.False(lease.TryBegin(ownerId, ListingSyncLeaseSource.AutoPublish, out _));
        Assert.False(lease.TryBegin(ownerId, ListingSyncLeaseSource.Background, out _));
    }
}
