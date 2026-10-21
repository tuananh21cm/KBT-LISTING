using Application.Services;
using Xunit;

namespace Application.Tests;

public class OrderSyncOwnerBatchTests
{
    [Fact]
    public void ChunkOwners_SplitsExactBatchesAndRemainder()
    {
        var owners = Enumerable.Range(1, 23).ToList();

        var batches = OrderSyncScheduleConfig.ChunkOwners(owners, 10).ToList();

        Assert.Equal(3, batches.Count);
        Assert.Equal(10, batches[0].Count);
        Assert.Equal(10, batches[1].Count);
        Assert.Equal(3, batches[2].Count);
        Assert.Equal(Enumerable.Range(1, 23), batches.SelectMany(b => b));
    }

    [Fact]
    public void ChunkOwners_NonPositiveBatchSize_MeansAllAtOnce()
    {
        var owners = Enumerable.Range(1, 7).ToList();

        var batches = OrderSyncScheduleConfig.ChunkOwners(owners, 0).ToList();

        Assert.Single(batches);
        Assert.Equal(7, batches[0].Count);
    }

    [Fact]
    public void ChunkOwners_Empty_YieldsNothing()
    {
        var batches = OrderSyncScheduleConfig.ChunkOwners(Array.Empty<int>(), 5).ToList();
        Assert.Empty(batches);
    }

    [Fact]
    public async Task ChunkOwners_BatchesRunSequentially()
    {
        // Mimics SyncCoreAsync: each batch awaits WhenAll before the next batch starts.
        var owners = Enumerable.Range(1, 6).ToList();
        var active = 0;
        var maxActive = 0;
        var gate = new object();

        foreach (var batch in OrderSyncScheduleConfig.ChunkOwners(owners, 2))
        {
            var tasks = batch.Select(async _ =>
            {
                lock (gate)
                {
                    active++;
                    maxActive = Math.Max(maxActive, active);
                }

                await Task.Delay(30);

                lock (gate)
                {
                    active--;
                }
            }).ToArray();

            await Task.WhenAll(tasks);
        }

        // Within a batch of 2, at most 2 run together; never more because batches are sequential.
        Assert.InRange(maxActive, 1, 2);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task ChunkOwners_FailureInOneBatch_DoesNotBlockNextBatch()
    {
        var owners = new[] { "ok-1", "fail", "ok-2", "ok-3" };
        var completed = new List<string>();

        foreach (var batch in OrderSyncScheduleConfig.ChunkOwners(owners, 2))
        {
            var partials = await Task.WhenAll(batch.Select(async id =>
            {
                await Task.Yield();
                if (id == "fail")
                {
                    return (Ok: false, Id: id);
                }

                return (Ok: true, Id: id);
            }));

            foreach (var partial in partials.Where(p => p.Ok))
            {
                completed.Add(partial.Id);
            }
        }

        Assert.Equal(new[] { "ok-1", "ok-2", "ok-3" }, completed);
    }
}
