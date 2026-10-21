using System.Text.Json;
using Application.DTOs.Category;
using Application.DTOs.Listing;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishOrchestratorTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const string SupplierSizeChartUrl = "https://supplier/size-chart.jpg";

    private readonly ApplicationDbContext _db;
    private readonly Guid _listingId = Guid.NewGuid();
    private readonly Guid _shopId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _supplierProductId = Guid.NewGuid();

    private readonly StubSettingProvider _settings = new();
    private readonly StubCategoryService _categories = new();
    private readonly StubDescriptionAiService _rewrite = new();
    private readonly StubSpecificsMapService _specifics = new();
    private readonly StubAutoPublishPrepService _prep = new();
    private readonly ListingSellerSyncLease _sellerLease = new();
    private readonly ListingAutoPublishCancellationCoordinator _cancellation;

    public ListingAutoPublishOrchestratorTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _cancellation = new ListingAutoPublishCancellationCoordinator(
            new ListingShopPublishLease(),
            NullLogger<ListingAutoPublishCancellationCoordinator>.Instance);

        _settings.Values[ListingAutoPublishSettingKeys.Enabled] = "true";
        _categories.Recommendation = new RecommendCategoryResultDto
        {
            CategoryId = "cat-1",
            CategoryName = "Pajamas"
        };
        _rewrite.Result = new RewriteListingDescriptionResultDto
        {
            Title = "Rewritten title",
            DescriptionHtml = "<p>rewritten</p>"
        };
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Run_HappyPath_MarksSuccessAndClearsCheckpoints()
    {
        await SeedAsync();
        var sync = CreateSync();
        var orchestrator = CreateOrchestrator(sync);

        var runId = await orchestrator.RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Success, run.Status);
        Assert.Null(run.ErrorMessage);
        Assert.Null(run.FailedStepCode);

        var steps = DeserializeSteps(run.StepsJson);
        Assert.Equal(ListingAutoPublishStepCodes.PipelineOrder, steps.Select(s => s.Code).ToArray());
        Assert.All(steps, s => Assert.True(s.Success));
        Assert.All(steps, s => Assert.False(s.InProgress));

        var loadFull = steps.Single(s => s.Code == ListingAutoPublishStepCodes.LoadFull);
        var syncSub = SubStepsOf(loadFull).Single(s => s.Code == "sync_4seller");
        Assert.True(syncSub.Success);
        Assert.False(syncSub.Skipped);
        Assert.Contains("GetByIdAsync:refresh", sync.Calls);

        var listing = await _db.Listings.AsNoTracking().FirstAsync(l => l.Id == _listingId);
        Assert.Null(listing.AutoPublishCompletedStepsJson);
        Assert.Equal(runId, listing.LastAutoPublishRunId);
        Assert.Equal(1, sync.PublishCount);
        Assert.Equal(ListingPublishSources.Auto, sync.LastPublishSource);
    }

    [Fact]
    public async Task Run_PersistsLiveStepsJsonWhileRunning()
    {
        await SeedAsync();
        var sync = CreateSync();
        var sawLiveProgress = false;

        _rewrite.OnRewrite = async () =>
        {
            var live = await _db.ListingAutoPublishRuns.FirstAsync(r => r.ListingId == _listingId);
            Assert.Equal(ListingAutoPublishRunStatuses.Running, live.Status);
            Assert.False(string.IsNullOrWhiteSpace(live.StepsJson));

            var steps = DeserializeSteps(live.StepsJson);
            Assert.Contains(steps, s =>
                s.Code == ListingAutoPublishStepCodes.LoadFull && s.Success && !s.InProgress);
            Assert.Contains(steps, s =>
                s.Code == ListingAutoPublishStepCodes.Rewrite && s.InProgress && !s.Success);
            sawLiveProgress = true;
            throw new BusinessException("stop mid rewrite");
        };

        var orchestrator = CreateOrchestrator(sync);
        var runId = await orchestrator.RunAsync(_listingId);

        Assert.True(sawLiveProgress);
        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Failed, run.Status);
        var finalSteps = DeserializeSteps(run.StepsJson);
        var rewrite = Assert.Single(finalSteps, s => s.Code == ListingAutoPublishStepCodes.Rewrite);
        Assert.False(rewrite.InProgress);
        Assert.False(rewrite.Success);
    }

    [Fact]
    public async Task Run_CancelAfterPublishAccepted_StaysSuccess()
    {
        await SeedAsync();
        var sync = CreateSync();

        // 4Seller already accepted the listing — a cancel landing during local finalisation
        // must not downgrade the run to "cancelled".
        sync.OnPublished = () =>
        {
            _cancellation.CancelShop(_shopId, "test_disable");
            return Task.CompletedTask;
        };

        var orchestrator = CreateOrchestrator(sync);
        var runId = await orchestrator.RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Success, run.Status);
        Assert.Equal(1, sync.PublishCount);

        var listing = await _db.Listings.AsNoTracking().FirstAsync(l => l.Id == _listingId);
        Assert.Null(listing.AutoPublishCompletedStepsJson);
    }

    [Fact]
    public async Task Run_CancelDuringRewrite_KeepsCheckpointsAndDoesNotSkipListing()
    {
        await SeedAsync();
        var sync = CreateSync();
        _rewrite.OnRewrite = () =>
        {
            _cancellation.CancelShop(_shopId, "test_disable");
            return Task.CompletedTask;
        };

        var orchestrator = CreateOrchestrator(sync);
        var runId = await orchestrator.RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Cancelled, run.Status);
        Assert.Equal(0, sync.PublishCount);

        var listing = await _db.Listings.AsNoTracking().FirstAsync(l => l.Id == _listingId);
        Assert.False(listing.AutoPublishSkipped);

        // Checkpoints up to the step before rewrite must survive the cancel.
        var codes = JsonSerializer.Deserialize<List<string>>(listing.AutoPublishCompletedStepsJson!, JsonOpts)!;
        Assert.Equal(
            [
                ListingAutoPublishStepCodes.LoadFull,
                ListingAutoPublishStepCodes.Category,
                ListingAutoPublishStepCodes.Commerce,
                ListingAutoPublishStepCodes.Specifics
            ],
            codes);
    }

    [Fact]
    public async Task Run_AlreadyRewritten_SkipsRewriteAndDoesNotCallOpenAi()
    {
        await SeedAsync();
        var sync = CreateSync(detail => detail.DescriptionRewritten = true);
        var orchestrator = CreateOrchestrator(sync);

        var runId = await orchestrator.RunAsync(_listingId);

        Assert.Equal(0, _rewrite.CallCount);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Success, run.Status);
        Assert.Equal(1, sync.PublishCount);

        var rewrite = DeserializeSteps(run.StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Rewrite);
        Assert.True(rewrite.Success);
        Assert.True(rewrite.Skipped);
        Assert.Contains(SubStepsOf(rewrite), s => s.Code == "already_rewritten" && s.Skipped);
    }

    [Fact]
    public async Task Run_NoKeyValuePairs_SkipsSpecificsAndStillPublishes()
    {
        await SeedAsync();
        _specifics.Result = new MapListingSpecificsResultDto { ParsedPairCount = 0 };
        var sync = CreateSync();
        var orchestrator = CreateOrchestrator(sync);

        var runId = await orchestrator.RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Success, run.Status);
        Assert.Null(run.FailedStepCode);
        Assert.Equal(1, sync.PublishCount);

        var specifics = DeserializeSteps(run.StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Specifics);
        Assert.True(specifics.Success);
        Assert.True(specifics.Skipped);
        Assert.Contains(SubStepsOf(specifics), s => s.Code == "no_key_value_pairs" && s.Skipped);

        var listing = await _db.Listings.AsNoTracking().FirstAsync(l => l.Id == _listingId);
        Assert.False(listing.AutoPublishSkipped);
    }

    [Fact]
    public async Task Run_ValidateFailure_RecordsSubStepsAndSkipsListing()
    {
        await SeedAsync();
        var sync = CreateSync(detail => detail.PackageWeight = null);
        _prep.ShippingAlreadyComplete = true;
        var orchestrator = CreateOrchestrator(sync);

        var runId = await orchestrator.RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Failed, run.Status);
        Assert.Equal(ListingAutoPublishStepCodes.Validate, run.FailedStepCode);
        Assert.Equal(0, sync.PublishCount);

        var validate = DeserializeSteps(run.StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Validate);
        Assert.False(validate.Success);
        Assert.Contains(SubStepsOf(validate), s => s.Code == "package_weight" && !s.Success);

        var listing = await _db.Listings.AsNoTracking().FirstAsync(l => l.Id == _listingId);
        Assert.True(listing.AutoPublishSkipped);
        Assert.Null(listing.AutoPublishCompletedStepsJson);
    }

    [Fact]
    public async Task Run_ValidateSubSteps_MarkWarehouseSkippedWhenOnlyShippingChanged()
    {
        await SeedAsync();
        var sync = CreateSync(detail => detail.WarehouseId = "wh-existing");
        _prep.ShippingAlreadyComplete = false;
        var orchestrator = CreateOrchestrator(sync);

        var runId = await orchestrator.RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        var validate = DeserializeSteps(run.StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Validate);

        var shipping = SubStepsOf(validate).Single(s => s.Code == "shipping_template");
        var warehouse = SubStepsOf(validate).Single(s => s.Code == "warehouse");
        Assert.False(shipping.Skipped);
        Assert.True(warehouse.Skipped);
    }

    [Fact]
    public async Task Run_AfterFailAndRetry_RerunsPipelineFromLoadFull()
    {
        await SeedAsync();

        // First pass fails at validate on a missing package weight — clears checkpoints.
        _prep.ShippingAlreadyComplete = true;
        await CreateOrchestrator(CreateSync(detail => detail.PackageWeight = null)).RunAsync(_listingId);

        var afterFail = await _db.Listings.AsNoTracking().FirstAsync(l => l.Id == _listingId);
        Assert.Null(afterFail.AutoPublishCompletedStepsJson);

        await new ListingAutoPublishRunService(_db).RetryAutoPublishAsync(_listingId);
        _db.ChangeTracker.Clear();

        // Second pass re-runs every step (including 4Seller refresh) then publishes.
        _prep.ShippingAlreadyComplete = false;
        _prep.Calls.Clear();
        var secondSync = CreateSync();
        var runId = await CreateOrchestrator(secondSync).RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Success, run.Status);

        var steps = DeserializeSteps(run.StepsJson);
        Assert.Equal(ListingAutoPublishStepCodes.PipelineOrder, steps.Select(s => s.Code).ToArray());
        Assert.All(steps, s => Assert.True(s.Success));

        var loadFull = steps.Single(s => s.Code == ListingAutoPublishStepCodes.LoadFull);
        Assert.False(loadFull.Skipped);
        var syncSub = SubStepsOf(loadFull).Single(s => s.Code == "sync_4seller");
        Assert.True(syncSub.Success);
        Assert.False(syncSub.Skipped);
        Assert.Contains("GetByIdAsync:refresh", secondSync.Calls);
        Assert.Contains(nameof(IListingAutoPublishPrepService.PrepareImages), _prep.Calls);
        Assert.Contains(nameof(IListingAutoPublishPrepService.GenerateBatchSkus), _prep.Calls);
        Assert.Equal(1, secondSync.PublishCount);
    }

    [Fact]
    public async Task Run_LoadFullFailure_KeepsSubStepDetailInLog()
    {
        await SeedAsync();

        // Empty mirror: no raw payload and no variations, so the sufficiency gate fails.
        var sync = CreateSync(detail =>
        {
            detail.HasRawPayload = false;
            detail.Variations.Clear();
        });
        var orchestrator = CreateOrchestrator(sync);

        var runId = await orchestrator.RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Failed, run.Status);
        Assert.Equal(ListingAutoPublishStepCodes.LoadFull, run.FailedStepCode);

        var loadFull = DeserializeSteps(run.StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.LoadFull);
        Assert.False(loadFull.Success);
        Assert.Equal(
            ["load_from_db", "sync_4seller"],
            SubStepsOf(loadFull).Select(s => s.Code).ToList());
        Assert.False(SubStepsOf(loadFull).Single(s => s.Code == "sync_4seller").Success);
    }

    [Fact]
    public async Task Run_ListingWithoutShop_ThrowsBusinessExceptionNotNullReference()
    {
        await SeedAsync(withShop: false);
        var sync = CreateSync();
        var orchestrator = CreateOrchestrator(sync);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => orchestrator.RunAsync(_listingId));

        Assert.Contains("shop", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _db.ListingAutoPublishRuns.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Run_MirrorDropsSizeChartCapture_StillUploadsFromSupplier()
    {
        await SeedAsync();

        // Reproduces the mirror round-trip that used to lose SizeChartCaptureUrl before the
        // media step ran, which silently failed the size_chart gate.
        var sync = CreateSync();
        sync.DropSizeChartCaptureOnUpdate = true;
        _prep.SizeChartInGallery = false;

        var orchestrator = CreateOrchestrator(sync);
        var runId = await orchestrator.RunAsync(_listingId);

        Assert.Contains(nameof(IListingAutoPublishPrepService.UploadSizeChartFromCaptureAsync), _prep.Calls);
        Assert.Equal(SupplierSizeChartUrl, _prep.LastSizeChartCaptureUrl);
        Assert.Equal(SupplierSizeChartUrl, _prep.LastPrepareImagesSizeChartUrl);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Success, run.Status);
    }

    [Fact]
    public async Task Run_PublishFailure_LeavesRunFailedWithPublishStep()
    {
        await SeedAsync();
        var sync = CreateSync();
        sync.PublishError = new BusinessException("4Seller từ chối payload.");
        var orchestrator = CreateOrchestrator(sync);

        var runId = await orchestrator.RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Failed, run.Status);
        Assert.Equal(ListingAutoPublishStepCodes.Publish, run.FailedStepCode);
        Assert.Equal("4Seller từ chối payload.", run.ErrorMessage);
        Assert.NotNull(run.FinishedAt);

        // Fail clears all checkpoints so the next pick/retry re-runs load_full → publish.
        var listing = await _db.Listings.AsNoTracking().FirstAsync(l => l.Id == _listingId);
        Assert.Null(listing.AutoPublishCompletedStepsJson);
    }

    [Fact]
    public async Task Run_CategoryAlreadySet_StillAsksAiAndRecordsConfirmed()
    {
        await SeedAsync();
        _categories.Recommendation = ValidatedRecommendation("cat-1", "Pajamas", "601152/842248/cat-1");
        var sync = CreateSync();

        var runId = await CreateOrchestrator(sync).RunAsync(_listingId);

        Assert.Equal(1, _categories.RecommendCallCount);
        Assert.Equal("Original title", _categories.RecommendedTitles.Single());

        var category = DeserializeSteps(
            (await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId)).StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Category);
        Assert.True(category.Success);
        Assert.Contains(
            SubStepsOf(category),
            s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.Confirmed);
        Assert.Contains(
            SubStepsOf(category),
            s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.AiResponse && s.Note!.Contains("cat-1"));
        Assert.Equal("cat-1", sync.Detail.CategoryId);
    }

    [Fact]
    public async Task Run_CategoryInRiskyBranchAndPlainTitle_OverridesWithAiAndClearsSpecifics()
    {
        await SeedAsync();

        // 4Seller's crawl parked a plain corset top in the costume branch, whose attribute enums
        // TikTok validates differently — the case that produced Specifics.Washing instructions.
        var sync = CreateSync(detail =>
        {
            detail.CategoryId = "903824";
            detail.CategoryName = "Costume Tops & Corsets";
            detail.ProductName = "Women's Lace Trim Corset Top";
            detail.Attributes.Add(new ListingAttributeDto
            {
                AttrId = "100401",
                AttrName = "Washing instructions",
                AttrValue = "Machine washable"
            });
        });
        _categories.Catalog["903824"] = CatalogRow("903824", "Costume Tops & Corsets", "601152/842632/843656/903824");
        _categories.Recommendation = ValidatedRecommendation(
            "843400",
            "Women's Tanks & Camis",
            "601152/842248/843400");

        var runId = await CreateOrchestrator(sync).RunAsync(_listingId);

        Assert.Equal("843400", sync.Detail.CategoryId);
        Assert.Equal("Women's Tanks & Camis", sync.Detail.CategoryName);

        // B2 is the first mirror write of the run, and it must carry no Specifics from 903824.
        Assert.Empty(sync.AttributeSnapshots[0]);

        var category = DeserializeSteps(
            (await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId)).StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Category);
        Assert.Contains(
            SubStepsOf(category),
            s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.Overridden
                 && s.Note!.Contains("903824")
                 && s.Note.Contains("843400"));
        Assert.Contains(
            SubStepsOf(category),
            s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.AttributesReset);

        // Specifics must map against the new schema, not the costume category it just left.
        Assert.Equal("843400", _specifics.MappedCategoryIds.Single());
    }

    [Fact]
    public async Task Run_BlockedCategoryWithCostumeTitle_OverridesWithAi()
    {
        await SeedAsync();
        var sync = CreateSync(detail =>
        {
            detail.CategoryId = "903824";
            detail.CategoryName = "Costume Tops & Corsets";
            detail.ProductName = "Women's Halloween Cosplay Costume Corset Top";
        });
        _categories.Catalog["903824"] = CatalogRow("903824", "Costume Tops & Corsets", "601152/842632/843656/903824");
        _categories.Recommendation = ValidatedRecommendation(
            "843400",
            "Women's Tanks & Camis",
            "601152/842248/843400");

        var runId = await CreateOrchestrator(sync).RunAsync(_listingId);

        Assert.Equal("843400", sync.Detail.CategoryId);
        var category = DeserializeSteps(
            (await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId)).StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Category);
        Assert.Contains(
            SubStepsOf(category),
            s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.Overridden
                 && s.Note!.Contains("blocked remap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Run_CategoryVerifyDisabled_OnBlockedLeaf_RemapsWithoutAi()
    {
        await SeedAsync();
        _settings.Values[ListingAutoPublishSettingKeys.CategoryVerifyEnabled] = "false";
        var sync = CreateSync(detail =>
        {
            detail.CategoryId = "903824";
            detail.CategoryName = "Costume Tops & Corsets";
            detail.Attributes.Add(new ListingAttributeDto
            {
                AttrId = "100401",
                AttrName = "Washing instructions",
                AttrValue = "Hand wash only"
            });
        });
        _categories.Catalog["903824"] = CatalogRow("903824", "Costume Tops & Corsets", "601152/842632/843656/903824");
        _categories.Catalog["903952"] = CatalogRow("903952", "Costume Sets", "601152/842632/843656/903952");

        var runId = await CreateOrchestrator(sync).RunAsync(_listingId);

        Assert.Equal(0, _categories.RecommendCallCount);
        Assert.Equal("903952", sync.Detail.CategoryId);
        Assert.Equal("Costume Sets", sync.Detail.CategoryName);
        Assert.Empty(sync.AttributeSnapshots[0]);

        var category = DeserializeSteps(
            (await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId)).StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Category);
        Assert.Contains(
            SubStepsOf(category),
            s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.BlockedRemapped && s.Success);
        Assert.Contains(
            SubStepsOf(category),
            s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.AttributesReset);
    }

    [Fact]
    public async Task Run_AiSuggestsOtherCategoryButCurrentIsSafe_KeepsCurrent()
    {
        await SeedAsync();
        var sync = CreateSync();
        _categories.Catalog["cat-1"] = CatalogRow("cat-1", "Pajamas", "601152/842248/cat-1");
        _categories.Recommendation = ValidatedRecommendation(
            "843400",
            "Women's Tanks & Camis",
            "601152/842248/843400");

        var runId = await CreateOrchestrator(sync).RunAsync(_listingId);

        Assert.Equal("cat-1", sync.Detail.CategoryId);
        var category = DeserializeSteps(
            (await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId)).StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Category);
        Assert.Contains(
            SubStepsOf(category),
            s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.Kept);
    }

    [Fact]
    public async Task Run_AiUnavailableWithExistingCategory_KeepsCategoryAndPublishes()
    {
        await SeedAsync();
        _categories.RecommendError = new BusinessException("4Seller AI hết cookie.");
        var sync = CreateSync();

        var runId = await CreateOrchestrator(sync).RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Success, run.Status);
        Assert.Equal(1, sync.PublishCount);
        Assert.Equal("cat-1", sync.Detail.CategoryId);

        var category = DeserializeSteps(run.StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Category);
        Assert.True(category.Success);
        var failure = SubStepsOf(category)
            .Single(s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.AiFailed);
        Assert.False(failure.Success);
        Assert.Equal("4Seller AI hết cookie.", failure.Error);
    }

    [Fact]
    public async Task Run_AiUnavailableWithoutCategory_FailsCategoryStep()
    {
        await SeedAsync();
        _categories.RecommendError = new BusinessException("4Seller AI hết cookie.");
        var sync = CreateSync(detail =>
        {
            detail.CategoryId = null;
            detail.CategoryName = null;
        });

        var runId = await CreateOrchestrator(sync).RunAsync(_listingId);

        var run = await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal(ListingAutoPublishRunStatuses.Failed, run.Status);
        Assert.Equal(ListingAutoPublishStepCodes.Category, run.FailedStepCode);
        Assert.Equal(0, sync.PublishCount);
    }

    [Fact]
    public async Task Run_CategoryVerifyDisabled_SkipsAiForExistingCategory()
    {
        await SeedAsync();
        _settings.Values[ListingAutoPublishSettingKeys.CategoryVerifyEnabled] = "false";
        var sync = CreateSync();

        var runId = await CreateOrchestrator(sync).RunAsync(_listingId);

        Assert.Equal(0, _categories.RecommendCallCount);
        var category = DeserializeSteps(
            (await _db.ListingAutoPublishRuns.AsNoTracking().FirstAsync(r => r.Id == runId)).StepsJson)
            .Single(s => s.Code == ListingAutoPublishStepCodes.Category);
        Assert.Contains(
            SubStepsOf(category),
            s => s.Code == ListingAutoPublishStepCodes.CategorySubSteps.Kept && s.Skipped);
    }

    private static RecommendCategoryResultDto ValidatedRecommendation(string id, string name, string path) =>
        new()
        {
            CategoryId = id,
            CategoryName = name,
            CategoryPath = path,
            PathIds = [.. path.Split('/')],
            IsLeaf = true,
            IsValidated = true,
            Site = "US"
        };

    private static CategoryDto CatalogRow(string id, string name, string path) =>
        new()
        {
            ExternalId = id,
            Name = name,
            Path = path,
            Site = "US",
            IsLeaf = true,
            IsActive = true
        };

    [Fact]
    public async Task Run_SystemAutoPublishDisabled_ThrowsAndCreatesNoRun()
    {
        await SeedAsync();
        _settings.Values[ListingAutoPublishSettingKeys.Enabled] = "false";
        var orchestrator = CreateOrchestrator(CreateSync());

        await Assert.ThrowsAsync<BusinessException>(() => orchestrator.RunAsync(_listingId));

        Assert.Empty(await _db.ListingAutoPublishRuns.AsNoTracking().ToListAsync());
    }

    private static List<ListingAutoPublishStepDto> DeserializeSteps(string? json) =>
        JsonSerializer.Deserialize<List<ListingAutoPublishStepDto>>(json!, JsonOpts) ?? [];

    private static List<ListingAutoPublishSubStepDto> SubStepsOf(ListingAutoPublishStepDto step)
    {
        Assert.NotNull(step.SubSteps);
        return step.SubSteps;
    }

    private StubListingSyncService CreateSync(Action<ListingDetailDto>? configure = null)
    {
        var detail = new ListingDetailDto
        {
            Id = _listingId,
            OwnerId = _ownerId,
            ShopId = _shopId,
            FourSellerListingId = 900001,
            ProductName = "Original title",
            DescriptionHtml = "<p>original</p>",
            Site = "US",
            CategoryId = "cat-1",
            CategoryName = "Pajamas",
            HasRawPayload = true,
            MainImage = "https://cdn/main.jpg",
            SizeChartCaptureUrl = SupplierSizeChartUrl,
            PackageWeight = 1m,
            PackageLength = 10m,
            PackageWidth = 8m,
            PackageHeight = 2m,
            Variations = [new ListingVariationDto { Id = Guid.NewGuid(), SellerSku = "SKU-1" }],
            Images = [new ListingImageDto { Url = "https://cdn/main.jpg", MediaType = "Main" }]
        };

        configure?.Invoke(detail);
        return new StubListingSyncService(detail);
    }

    private ListingAutoPublishOrchestrator CreateOrchestrator(StubListingSyncService sync) =>
        new(
            _db,
            _settings,
            sync,
            _categories,
            _rewrite,
            _specifics,
            _prep,
            new ListingAutoPublishStepCheckpointService(_db),
            _sellerLease,
            _cancellation,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ListingAutoPublishOrchestrator>.Instance);

    private async Task SeedAsync(bool withShop = true)
    {
        _db.SupplierProductInfos.Add(new SupplierProductInfo
        {
            Id = _supplierProductId,
            SourceUrlKey = "supplier-key",
            SizeChartImageUrl = SupplierSizeChartUrl
        });

        _db.Shops.Add(new Shop
        {
            Id = _shopId,
            OwnerId = _ownerId,
            Status = ShopStatuses.Active,
            AutoPublishEnabled = true,
            FourSellerShopId = "4s-shop-1"
        });

        _db.Listings.Add(new Listing
        {
            Id = _listingId,
            OwnerId = _ownerId,
            ShopId = withShop ? _shopId : null,
            PageType = ListingPageTypes.Draft,
            PublishStatus = PublishStatuses.Draft,
            ProductName = "Original title",
            SupplierProductInfoId = _supplierProductId
        });

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
