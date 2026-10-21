using Application.Services;
using Xunit;

namespace Application.Tests;

public class PostHtmlDraftSyncSchedulerTests
{
    [Fact]
    public void BusyRetryBudget_CoversBackgroundListingSyncWindow()
    {
        var budget = PostHtmlDraftSyncScheduler.BusyRetryDelay.TotalSeconds
            * PostHtmlDraftSyncScheduler.BusyRetryMaxAttempts;
        Assert.True(budget >= 180, $"expected ≥3 minutes of busy retries, got {budget}s");
        Assert.Equal(12, PostHtmlDraftSyncScheduler.DefaultDebounce.TotalSeconds);
    }
}
