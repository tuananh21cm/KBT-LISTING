using Application.DTOs.Shop;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class TikTokShopHealthRecordServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly TikTokShopHealthRecordService _service;

    public TikTokShopHealthRecordServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new TikTokShopHealthRecordService(
            _db,
            new TikTokShopSyncLogWriter(_db, NullLogger<TikTokShopSyncLogWriter>.Instance));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SyncAsync_InsertsNewRecords()
    {
        var ownerId = Guid.NewGuid();
        var shop = await SeedShopAsync(ownerId, "seller-1");

        var result = await _service.SyncAsync(ownerId, new SyncTikTokShopHealthRecordsDto
        {
            ExternalShopId = "seller-1",
            Items =
            [
                new SyncTikTokShopHealthRecordItemDto
                {
                    RecordId = "rec-1",
                    RecordType = TikTokShopHealthRecordTypes.Violation,
                    Reason = "High Customer Complaint Rate",
                    Detail = "Detail text",
                    CategoryType = "Policy Compliance",
                    Actions =
                    [
                        new SyncHealthActionDto
                        {
                            Title = "Settlement period is extended",
                            ActionStartTime = 1_787_387_290,
                            ActionEndTime = 1_790_066_890
                        }
                    ],
                    RecordStatus = TikTokShopHealthRecordStatuses.Active,
                    CreateTime = 1_787_387_290
                }
            ]
        });

        Assert.Equal(shop.Id, result.ShopId);
        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Updated);

        var row = await _db.TikTokShopHealthRecords.SingleAsync();
        Assert.Equal("rec-1", row.RecordId);
        Assert.Equal(TikTokShopHealthRecordTypes.Violation, row.RecordType);
        Assert.Contains("Settlement period is extended", row.ActionTitlesJson);
        Assert.Contains("actionStartTime", row.ActionTitlesJson);
        Assert.Contains("actionEndTime", row.ActionTitlesJson);
    }

    [Fact]
    public async Task SyncAsync_PersistsActionTimesAndMapsBrief()
    {
        var ownerId = Guid.NewGuid();
        var shop = await SeedShopAsync(ownerId, "seller-1");

        await _service.SyncAsync(ownerId, new SyncTikTokShopHealthRecordsDto
        {
            ExternalShopId = "seller-1",
            Items =
            [
                new SyncTikTokShopHealthRecordItemDto
                {
                    RecordId = "rec-actions",
                    RecordType = TikTokShopHealthRecordTypes.Violation,
                    Reason = "Test",
                    Actions =
                    [
                        new SyncHealthActionDto
                        {
                            Title = "Shop visibility reduced",
                            ActionStartTime = 1_787_387_290,
                            ActionEndTime = 1_790_066_890
                        }
                    ],
                    RecordStatus = TikTokShopHealthRecordStatuses.Active,
                    CreateTime = 1_787_387_290
                }
            ]
        });

        var summaries = await _service.GetSummariesForShopsAsync([shop.Id]);
        Assert.True(summaries.TryGetValue(shop.Id, out var summary));
        var record = Assert.Single(summary.PendingRecent);
        var action = Assert.Single(record.Actions);
        Assert.Equal("Shop visibility reduced", action.Title);
        Assert.NotNull(action.ActionStartTime);
        Assert.NotNull(action.ActionEndTime);
    }

    [Fact]
    public async Task GetSummariesForShopsAsync_DeserializesLegacyStringArrayActions()
    {
        var ownerId = Guid.NewGuid();
        var shop = await SeedShopAsync(ownerId, "seller-legacy");

        _db.TikTokShopHealthRecords.Add(
            new TikTokShopHealthRecord
            {
                ShopId = shop.Id,
                RecordId = "legacy-1",
                RecordType = TikTokShopHealthRecordTypes.Violation,
                Reason = "Legacy",
                Detail = "",
                CategoryType = "Policy",
                ActionTitlesJson = "[\"Settlement period is extended\"]",
                RecordStatus = TikTokShopHealthRecordStatuses.Active,
                CreateTime = DateTime.UtcNow,
                FirstSyncedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        var summaries = await _service.GetSummariesForShopsAsync([shop.Id]);
        Assert.True(summaries.TryGetValue(shop.Id, out var summary));
        var record = Assert.Single(summary.PendingRecent);
        var action = Assert.Single(record.Actions);
        Assert.Equal("Settlement period is extended", action.Title);
        Assert.Null(action.ActionStartTime);
        Assert.Null(action.ActionEndTime);
    }

    [Fact]
    public async Task SyncAsync_UpdatesExistingRecordWithoutDuplicateRow()
    {
        var ownerId = Guid.NewGuid();
        var shop = await SeedShopAsync(ownerId, "seller-1");

        var dto = new SyncTikTokShopHealthRecordsDto
        {
            ExternalShopId = "seller-1",
            Items =
            [
                new SyncTikTokShopHealthRecordItemDto
                {
                    RecordId = "rec-1",
                    RecordType = TikTokShopHealthRecordTypes.Violation,
                    Reason = "Old reason",
                    RecordStatus = TikTokShopHealthRecordStatuses.Active,
                    CreateTime = 1_787_387_290
                }
            ]
        };

        await _service.SyncAsync(ownerId, dto);
        dto.Items![0].Reason = "New reason";
        dto.Items[0].RecordStatus = 2;

        var result = await _service.SyncAsync(ownerId, dto);

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Inserted);
        Assert.Equal(1, await _db.TikTokShopHealthRecords.CountAsync());

        var row = await _db.TikTokShopHealthRecords.SingleAsync();
        Assert.Equal("New reason", row.Reason);
        Assert.Equal((short)2, row.RecordStatus);
    }

    [Fact]
    public void StripHtml_RemovesTagsAndKeepsText()
    {
        var plain = TikTokShopHealthRecordService.StripHtml(
            "Line one<br/>Line two <a href=\"x\">link</a> &amp; more");

        Assert.Contains("Line one", plain);
        Assert.Contains("Line two", plain);
        Assert.DoesNotContain("<a", plain);
        Assert.Contains("&", plain);
    }

    [Fact]
    public async Task GetSummariesForShopsAsync_CountsOnlyActiveRecords()
    {
        var ownerId = Guid.NewGuid();
        var shop = await SeedShopAsync(ownerId, "seller-1");

        _db.TikTokShopHealthRecords.AddRange(
            new TikTokShopHealthRecord
            {
                ShopId = shop.Id,
                RecordId = "v1",
                RecordType = TikTokShopHealthRecordTypes.Violation,
                Reason = "Violation",
                Detail = "",
                CategoryType = "Policy",
                ActionTitlesJson = "[]",
                RecordStatus = TikTokShopHealthRecordStatuses.Active,
                CreateTime = DateTime.UtcNow.AddDays(-1),
                FirstSyncedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow
            },
            new TikTokShopHealthRecord
            {
                ShopId = shop.Id,
                RecordId = "w1",
                RecordType = TikTokShopHealthRecordTypes.Warning,
                Reason = "Warning",
                Detail = "",
                CategoryType = "Fulfillment",
                ActionTitlesJson = "[]",
                RecordStatus = TikTokShopHealthRecordStatuses.Active,
                CreateTime = DateTime.UtcNow,
                FirstSyncedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow
            },
            new TikTokShopHealthRecord
            {
                ShopId = shop.Id,
                RecordId = "v-old",
                RecordType = TikTokShopHealthRecordTypes.Violation,
                Reason = "Resolved",
                Detail = "",
                CategoryType = "Policy",
                ActionTitlesJson = "[]",
                RecordStatus = 2,
                CreateTime = DateTime.UtcNow.AddDays(-2),
                FirstSyncedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        var summaries = await _service.GetSummariesForShopsAsync([shop.Id]);

        Assert.True(summaries.TryGetValue(shop.Id, out var summary));
        Assert.Equal(1, summary.ActiveViolationCount);
        Assert.Equal(1, summary.ActiveWarningCount);
        Assert.Equal(2, summary.PendingRecent.Count);
        Assert.Empty(summary.HandledRecent);
    }

    [Fact]
    public async Task MarkHandledAsync_SetsHandledAtAndMovesOutOfPendingSummary()
    {
        var ownerId = Guid.NewGuid();
        var shop = await SeedShopAsync(ownerId, "seller-1");

        _db.TikTokShopHealthRecords.Add(
            new TikTokShopHealthRecord
            {
                ShopId = shop.Id,
                RecordId = "v1",
                RecordType = TikTokShopHealthRecordTypes.Violation,
                Reason = "Violation",
                Detail = "",
                CategoryType = "Policy",
                ActionTitlesJson = "[]",
                RecordStatus = TikTokShopHealthRecordStatuses.Active,
                CreateTime = DateTime.UtcNow,
                FirstSyncedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        await _service.MarkHandledAsync(ownerId, new MarkShopHealthRecordHandledDto
        {
            ShopId = shop.Id,
            RecordId = "v1"
        });

        var summaries = await _service.GetSummariesForShopsAsync([shop.Id]);
        Assert.True(summaries.TryGetValue(shop.Id, out var summary));
        Assert.Equal(0, summary.ActiveViolationCount);
        Assert.Equal(1, summary.HandledViolationCount);
        Assert.Empty(summary.PendingRecent);
        Assert.Single(summary.HandledRecent);
        Assert.NotNull(summary.HandledRecent[0].HandledAt);
    }

    private async Task<Shop> SeedShopAsync(Guid ownerId, string externalShopId)
    {
        var shop = new Shop
        {
            OwnerId = ownerId,
            Name = "Test Shop",
            ExternalShopId = externalShopId,
            Status = ShopStatuses.Active
        };
        _db.Shops.Add(shop);
        await _db.SaveChangesAsync();
        return shop;
    }
}
