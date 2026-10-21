using Application.Services;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishDispatchTriggerTests
{
    [Fact]
    public async Task Signal_WakesWaitAsync()
    {
        var trigger = new ListingAutoPublishDispatchTrigger();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var waitTask = trigger.WaitAsync(cts.Token);
        trigger.Signal();

        await waitTask;
    }

    [Fact]
    public async Task MultipleSignals_CoalesceWithoutThrowing()
    {
        var trigger = new ListingAutoPublishDispatchTrigger();

        trigger.Signal();
        trigger.Signal();
        trigger.Signal();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await trigger.WaitAsync(cts.Token);
        await trigger.WaitAsync(cts.Token);
        await trigger.WaitAsync(cts.Token);
    }
}
