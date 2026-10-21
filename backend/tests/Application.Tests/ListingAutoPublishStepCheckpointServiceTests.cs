using System.Text.Json;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishStepCheckpointServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ApplicationDbContext _db;
    private readonly ListingAutoPublishStepCheckpointService _service;

    public ListingAutoPublishStepCheckpointServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new ListingAutoPublishStepCheckpointService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task MarkCompleted_PersistsOrderedStepCodes()
    {
        var listingId = Guid.NewGuid();
        _db.Listings.Add(new Listing
        {
            Id = listingId,
            OwnerId = Guid.NewGuid(),
            PageType = ListingPageTypes.Draft,
            ProductName = "Test"
        });
        await _db.SaveChangesAsync();

        await _service.MarkCompletedAsync(listingId, ListingAutoPublishStepCodes.Commerce);
        await _service.MarkCompletedAsync(listingId, ListingAutoPublishStepCodes.LoadFull);

        var updated = await _db.Listings.FirstAsync(l => l.Id == listingId);
        var codes = JsonSerializer.Deserialize<List<string>>(updated.AutoPublishCompletedStepsJson!, JsonOpts);
        Assert.Equal(
            [ListingAutoPublishStepCodes.LoadFull, ListingAutoPublishStepCodes.Commerce],
            codes);
    }

    [Fact]
    public async Task EnsureLoaded_EmptyJson_DoesNotHydrateFromLastFailedRun()
    {
        var listingId = Guid.NewGuid();
        _db.Listings.Add(new Listing
        {
            Id = listingId,
            OwnerId = Guid.NewGuid(),
            PageType = ListingPageTypes.Draft,
            ProductName = "Test"
        });
        var stepsJson = JsonSerializer.Serialize(
            new List<object>
            {
                new { code = ListingAutoPublishStepCodes.LoadFull, success = true, skipped = false },
                new { code = ListingAutoPublishStepCodes.Category, success = true, skipped = false },
                new { code = ListingAutoPublishStepCodes.Commerce, success = false, skipped = false, error = "fail" }
            },
            JsonOpts);
        _db.ListingAutoPublishRuns.Add(new ListingAutoPublishRun
        {
            ListingId = listingId,
            ShopId = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Status = ListingAutoPublishRunStatuses.Failed,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            StepsJson = stepsJson
        });
        await _db.SaveChangesAsync();

        var loaded = await _service.EnsureLoadedAsync(listingId);

        Assert.Empty(loaded);
        var updated = await _db.Listings.FirstAsync(l => l.Id == listingId);
        Assert.Null(updated.AutoPublishCompletedStepsJson);
    }

    [Fact]
    public async Task Clear_RemovesPersistedCheckpoints()
    {
        var listingId = Guid.NewGuid();
        _db.Listings.Add(new Listing
        {
            Id = listingId,
            OwnerId = Guid.NewGuid(),
            PageType = ListingPageTypes.Draft,
            AutoPublishCompletedStepsJson = "[\"load_full\"]",
            ProductName = "Test"
        });
        await _db.SaveChangesAsync();

        await _service.ClearAsync(listingId);

        var updated = await _db.Listings.FirstAsync(l => l.Id == listingId);
        Assert.Null(updated.AutoPublishCompletedStepsJson);
    }

    [Fact]
    public async Task EnsureLoaded_ClearsCheckpointsWhenListingNotDraft()
    {
        var listingId = Guid.NewGuid();
        _db.Listings.Add(new Listing
        {
            Id = listingId,
            OwnerId = Guid.NewGuid(),
            PageType = ListingPageTypes.Published,
            AutoPublishCompletedStepsJson = "[\"load_full\"]",
            ProductName = "Test"
        });
        await _db.SaveChangesAsync();

        var loaded = await _service.EnsureLoadedAsync(listingId);

        Assert.Empty(loaded);
        var updated = await _db.Listings.FirstAsync(l => l.Id == listingId);
        Assert.Null(updated.AutoPublishCompletedStepsJson);
    }

    [Fact]
    public async Task InvalidateFrom_RemovesStepAndLaterCodes()
    {
        var listingId = Guid.NewGuid();
        _db.Listings.Add(new Listing
        {
            Id = listingId,
            OwnerId = Guid.NewGuid(),
            PageType = ListingPageTypes.Draft,
            AutoPublishCompletedStepsJson =
                "[\"load_full\",\"category\",\"commerce\",\"specifics\"]",
            ProductName = "Test"
        });
        await _db.SaveChangesAsync();

        await _service.InvalidateFromAsync(listingId, ListingAutoPublishStepCodes.Commerce);

        var updated = await _db.Listings.FirstAsync(l => l.Id == listingId);
        var codes = JsonSerializer.Deserialize<List<string>>(updated.AutoPublishCompletedStepsJson!, JsonOpts);
        Assert.Equal(
            [ListingAutoPublishStepCodes.LoadFull, ListingAutoPublishStepCodes.Category],
            codes);
    }
}
