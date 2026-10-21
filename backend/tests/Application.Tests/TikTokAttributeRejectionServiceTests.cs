using System.Text.Json;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// The denylist is knowledge learned from publish failures, so it must be written once and read
/// cheaply: every sync pass re-reads the same <c>failedMessage</c> off a listing that is still failed.
/// </summary>
public class TikTokAttributeRejectionServiceTests : IDisposable
{
    private const string CategoryId = "903824";
    private const string RealError =
        "【Publish Failed】Operation Not Allowed. Cannot use custom value names for product attribute "
        + "`Washing instructions`: attribute ID `100401` does not support custom value names. Remove or "
        + "replace the custom value `Do not dry clean` and retry.";

    private readonly ApplicationDbContext _db;
    private readonly TikTokAttributeRejectionService _sut;

    public TikTokAttributeRejectionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _sut = new TikTokAttributeRejectionService(
            _db,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<TikTokAttributeRejectionService>.Instance);
    }

    /// <summary>
    /// "Cannot use custom value names" says the value reached TikTok as bare text, which only happens
    /// when our row gave 4Seller no id it could resolve. It is a payload defect, so the failure is
    /// recorded for diagnosis and must never block the value.
    /// </summary>
    [Fact]
    public async Task RecordFromErrorAsync_RecordsTheValueForDiagnosisWithoutBlockingIt()
    {
        var listingId = await SeedListingAsync(valueId: "1001198", valueName: "Do not dry clean");

        var learned = await _sut.RecordFromErrorAsync(listingId, CategoryId, RealError);

        Assert.True(learned);
        var row = Assert.Single(await _db.TikTokAttributeValueRejections.ToListAsync());
        Assert.Equal(CategoryId, row.CategoryId);
        Assert.Equal("100401", row.AttributeId);
        Assert.Equal("Washing instructions", row.AttributeName);
        Assert.Equal("Do not dry clean", row.ValueName);
        Assert.Equal("1001198", row.ValueId);
        Assert.Equal(TikTokAttributeRejectionSources.MissingValueId, row.Source);
        Assert.Equal(1, row.RejectionCount);
        Assert.False((await _sut.GetDenylistAsync(CategoryId)).IsRejected("100401", "Do not dry clean"));
    }

    /// <summary>
    /// The regression this rule exists for: category 903824 ended up with five of the six Washing
    /// instructions values blocked, so a mandatory attribute had nothing left to send and the failure
    /// turned into <c>Specifics.Washing instructions</c>. Meanwhile a listing in a sibling category
    /// published happily with 1001195.
    /// </summary>
    [Fact]
    public async Task RecordFromErrorAsync_RepeatedRefusals_NeverEmptyTheEnum()
    {
        foreach (var valueName in new[]
                 {
                     "Do not dry clean", "Hand wash only", "Machine washable", "Cold wash", "Dry clean"
                 })
        {
            var listingId = await SeedListingAsync(valueId: "1001198", valueName: valueName);
            await _sut.RecordFromErrorAsync(
                listingId,
                CategoryId,
                RealError.Replace("Do not dry clean", valueName));
        }

        var denylist = await _sut.GetDenylistAsync(CategoryId);
        Assert.Equal(5, (await _db.TikTokAttributeValueRejections.ToListAsync()).Count);
        Assert.False(denylist.IsRejected("100401", "Hand wash only"));
        Assert.False(denylist.IsRejected("100401", "Machine washable"));
    }

    [Fact]
    public async Task RecordFromErrorAsync_IsIdempotentAcrossSyncPasses()
    {
        var listingId = await SeedListingAsync(valueId: "1001198", valueName: "Do not dry clean");
        await _sut.RecordFromErrorAsync(listingId, CategoryId, RealError);

        // A failed listing keeps its SyncError, so sync hands the same message over again on every
        // pass. Rewriting the row each time would flush the cache the publish path depends on and
        // turn RejectionCount into a count of sync passes.
        var relearned = await _sut.RecordFromErrorAsync(listingId, CategoryId, RealError);

        Assert.False(relearned);
        var row = Assert.Single(await _db.TikTokAttributeValueRejections.ToListAsync());
        Assert.Equal(1, row.RejectionCount);
    }

    /// <summary>
    /// One attribute id carries a different enum per category — 100401 lists six values on 903824 and
    /// more elsewhere — so a block must never reach beyond the category that produced it.
    /// </summary>
    [Fact]
    public async Task GetDenylistAsync_BlocksTheValueOnlyInTheCategoryThatRejectedIt()
    {
        await SeedBlockedValueAsync("100401", "Do not dry clean");

        Assert.True((await _sut.GetDenylistAsync(CategoryId)).IsRejected("100401", "Do not dry clean"));
        Assert.False((await _sut.GetDenylistAsync("601302")).IsRejected("100401", "Do not dry clean"));
    }

    [Fact]
    public async Task GetDenylistAsync_MatchesValueNameIgnoringCaseAndSpacing()
    {
        await SeedBlockedValueAsync("100401", "Do not dry clean");
        var denylist = await _sut.GetDenylistAsync(CategoryId);

        Assert.True(denylist.IsRejected("100401", "DO NOT DRY CLEAN"));
        Assert.True(denylist.IsRejected("100401", " Do  not dry clean "));
        Assert.False(denylist.IsRejected("100401", "Hand wash only"));
        Assert.False(denylist.IsRejected("101303", "Do not dry clean"));
    }

    [Fact]
    public async Task RecordFromErrorAsync_PayloadHadNoValueId_RecordsForDiagnosisWithoutBlocking()
    {
        // What poisoned the live denylist: we sent "Machine washable" as a bare name because the
        // mapping found no id, TikTok called it a custom value, and blocking it emptied a mandatory
        // attribute — the publish error then became "Specifics.Washing instructions".
        var listingId = await SeedListingAsync(valueId: null, valueName: "Machine washable");
        var error = RealError.Replace("Do not dry clean", "Machine washable");

        var recorded = await _sut.RecordFromErrorAsync(listingId, CategoryId, error);

        Assert.True(recorded);
        var row = Assert.Single(await _db.TikTokAttributeValueRejections.ToListAsync());
        Assert.Equal(TikTokAttributeRejectionSources.MissingValueId, row.Source);
        Assert.Null(row.ValueId);
        Assert.False((await _sut.GetDenylistAsync(CategoryId)).IsRejected("100401", "Machine washable"));
    }

    [Fact]
    public async Task RecordFromErrorAsync_UnknownListing_RecordsForDiagnosisWithoutBlocking()
    {
        var recorded = await _sut.RecordFromErrorAsync(Guid.NewGuid(), CategoryId, RealError);

        Assert.True(recorded);
        var row = Assert.Single(await _db.TikTokAttributeValueRejections.ToListAsync());
        Assert.Equal(TikTokAttributeRejectionSources.MissingValueId, row.Source);
        Assert.False((await _sut.GetDenylistAsync(CategoryId)).IsRejected("100401", "Do not dry clean"));
    }

    /// <summary>
    /// A row an older build promoted to blocking is demoted the next time the same message arrives:
    /// that message never justified blocking, so leaving it in place would keep the attribute empty.
    /// </summary>
    [Fact]
    public async Task RecordFromErrorAsync_DemotesARowAnOlderBuildBlocked()
    {
        await SeedBlockedValueAsync("100401", "Do not dry clean", sampleListingId: Guid.NewGuid());
        var listingId = await SeedListingAsync(valueId: "1001198", valueName: "Do not dry clean");

        await _sut.RecordFromErrorAsync(listingId, CategoryId, RealError);

        var row = Assert.Single(await _db.TikTokAttributeValueRejections.ToListAsync());
        Assert.Equal(TikTokAttributeRejectionSources.MissingValueId, row.Source);
        Assert.False((await _sut.GetDenylistAsync(CategoryId)).IsRejected("100401", "Do not dry clean"));
    }

    [Fact]
    public async Task RecordFromErrorAsync_IgnoresFailuresThatNameNoAttributeValue()
    {
        Assert.False(await _sut.RecordFromErrorAsync(
            Guid.NewGuid(),
            CategoryId,
            "【Publish Failed】Specifics.Region of origin"));
        Assert.Empty(await _db.TikTokAttributeValueRejections.ToListAsync());
    }

    [Fact]
    public async Task RecordFromErrorAsync_SkipsListingWithoutCategory()
    {
        // Recording it against no category would have to block the value everywhere, which is exactly
        // the over-reach that broke the previous hardcoded denylist.
        Assert.False(await _sut.RecordFromErrorAsync(Guid.NewGuid(), null, RealError));
        Assert.Empty(await _db.TikTokAttributeValueRejections.ToListAsync());
    }

    [Fact]
    public async Task RecordAcceptedAsync_ListingWentLiveWithTheValue_UnblocksIt()
    {
        await SeedBlockedValueAsync("100401", "Do not dry clean");
        Assert.True((await _sut.GetDenylistAsync(CategoryId)).IsRejected("100401", "Do not dry clean"));

        // What the scan of live listings can miss: a busy category's newest payloads need not mention
        // the value at all. The payload TikTok just accepted is the direct proof.
        var cleared = await _sut.RecordAcceptedAsync(
            Guid.NewGuid(),
            CategoryId,
            BuildRawPayload(CategoryId, "1001198", "Do not dry clean"));

        Assert.Equal(1, cleared);
        var row = Assert.Single(await _db.TikTokAttributeValueRejections.ToListAsync());
        Assert.Equal(TikTokAttributeRejectionSources.ContradictedByLive, row.Source);
        Assert.False((await _sut.GetDenylistAsync(CategoryId)).IsRejected("100401", "Do not dry clean"));
    }

    [Fact]
    public async Task RecordAcceptedAsync_PayloadWithoutTheValue_LeavesTheBlockInPlace()
    {
        await SeedBlockedValueAsync("100401", "Do not dry clean");

        var cleared = await _sut.RecordAcceptedAsync(
            Guid.NewGuid(),
            CategoryId,
            BuildRawPayload(CategoryId, "1001195", "Hand wash only"));

        Assert.Equal(0, cleared);
        Assert.True((await _sut.GetDenylistAsync(CategoryId)).IsRejected("100401", "Do not dry clean"));
    }

    [Fact]
    public async Task RecordAcceptedAsync_IsIdempotent()
    {
        await SeedBlockedValueAsync("100401", "Do not dry clean");
        var payload = BuildRawPayload(CategoryId, "1001198", "Do not dry clean");

        Assert.Equal(1, await _sut.RecordAcceptedAsync(Guid.NewGuid(), CategoryId, payload));

        // Every sync pass hands the same live listing over again; a demoted row has nothing left to say.
        Assert.Equal(0, await _sut.RecordAcceptedAsync(Guid.NewGuid(), CategoryId, payload));
    }

    [Fact]
    public async Task RemoveAsync_UnblocksTheValueForTheNextPublish()
    {
        await SeedBlockedValueAsync("100401", "Do not dry clean");
        var row = Assert.Single(await _sut.ListAsync());

        Assert.True(await _sut.RemoveAsync(row.Id));

        Assert.False((await _sut.GetDenylistAsync(CategoryId)).IsRejected("100401", "Do not dry clean"));
        Assert.False(await _sut.RemoveAsync(row.Id));
    }

    /// <summary>
    /// Seeds a failed listing whose RawPayload carries the washing value the way a real push would —
    /// with or without the numeric <c>valueId</c> that decides whether a rejection is evidence.
    /// </summary>
    private async Task<Guid> SeedListingAsync(string? valueId, string valueName)
    {
        var listingId = Guid.NewGuid();
        _db.Listings.Add(new Listing
        {
            Id = listingId,
            OwnerId = Guid.NewGuid(),
            CategoryId = CategoryId,
            ProductName = "Corset top",
            PageType = ListingPageTypes.Draft,
            PublishStatus = PublishStatuses.Failed,
            SyncError = RealError,
            RawPayload = BuildRawPayload(CategoryId, valueId, valueName)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return listingId;
    }

    /// <summary>
    /// Writes a row that actually blocks, the way an older build or an admin would have left it —
    /// the starting state for the reads and the demotion.
    /// </summary>
    private async Task SeedBlockedValueAsync(
        string attributeId,
        string valueName,
        Guid? sampleListingId = null)
    {
        _db.TikTokAttributeValueRejections.Add(new TikTokAttributeValueRejection
        {
            Id = Guid.NewGuid(),
            CategoryId = CategoryId,
            AttributeId = attributeId,
            AttributeName = "Washing instructions",
            ValueId = "1001198",
            ValueName = valueName,
            ValueNameNorm = ListingSpecificsMappingEngine.NormalizeKey(valueName),
            RejectionCount = 1,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            Source = TikTokAttributeRejectionSources.PublishError,
            SampleListingId = sampleListingId,
            SampleError = RealError
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static string BuildRawPayload(string categoryId, string? valueId, string valueName)
    {
        var productAttributes =
            $$"""
            [{"attributeId":"100401","attributeName":"Washing instructions",
              "values":[{"valueId":"{{valueId ?? string.Empty}}","valueName":"{{valueName}}","isShow":"true"}]}]
            """;
        return $$"""{"categoryId":"{{categoryId}}","productAttributes":{{JsonSerializer.Serialize(productAttributes)}}}""";
    }

    public void Dispose() => _db.Dispose();
}
