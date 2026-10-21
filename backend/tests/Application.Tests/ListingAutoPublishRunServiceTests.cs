using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishRunServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ListingAutoPublishRunService _service;

    public ListingAutoPublishRunServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new ListingAutoPublishRunService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Retry_ClearsStepCheckpoints()
    {
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            PageType = ListingPageTypes.Draft,
            AutoPublishSkipped = true,
            AutoPublishSkipReason = "fail",
            AutoPublishCompletedStepsJson = "[\"load_full\",\"category\"]",
            ProductName = "Test"
        };
        _db.Listings.Add(listing);
        await _db.SaveChangesAsync();

        await _service.RetryAutoPublishAsync(listing.Id);

        var updated = await _db.Listings.FirstAsync(l => l.Id == listing.Id);
        Assert.False(updated.AutoPublishSkipped);
        Assert.Null(updated.AutoPublishCompletedStepsJson);
    }

    [Fact]
    public async Task Retry_ClearsSkipForDraft()
    {
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            PageType = ListingPageTypes.Draft,
            AutoPublishSkipped = true,
            AutoPublishSkipReason = "fail",
            ProductName = "Test"
        };
        _db.Listings.Add(listing);
        await _db.SaveChangesAsync();

        await _service.RetryAutoPublishAsync(listing.Id);

        var updated = await _db.Listings.FirstAsync(l => l.Id == listing.Id);
        Assert.False(updated.AutoPublishSkipped);
        Assert.Null(updated.AutoPublishSkipReason);
    }

    [Fact]
    public async Task Retry_RejectsWhenActiveRunExists()
    {
        var listingId = Guid.NewGuid();
        _db.Listings.Add(new Listing
        {
            Id = listingId,
            OwnerId = Guid.NewGuid(),
            PageType = ListingPageTypes.Draft,
            AutoPublishSkipped = true,
            ProductName = "Test"
        });
        _db.ListingAutoPublishRuns.Add(new ListingAutoPublishRun
        {
            ListingId = listingId,
            ShopId = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Status = ListingAutoPublishRunStatuses.Running,
            StartedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() => _service.RetryAutoPublishAsync(listingId));
    }
}
