using System.Diagnostics;
using System.Text.Json;
using Application.DTOs.Category;
using Application.DTOs.Listing;
using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>Runs auto publish prep pipeline then pushes to 4Seller.</summary>
public sealed class ListingAutoPublishOrchestrator : IListingAutoPublishOrchestrator
{
    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settings;
    private readonly IListingSyncService _listingSync;
    private readonly ICategoryService _categoryService;
    private readonly IListingDescriptionAiService _rewrite;
    private readonly IListingSpecificsMapService _specifics;
    private readonly IListingAutoPublishPrepService _prep;
    private readonly IListingAutoPublishStepCheckpointService _checkpoints;
    private readonly IListingSellerSyncLease _sellerLease;
    private readonly IListingAutoPublishCancellationCoordinator _cancellation;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ListingAutoPublishOrchestrator> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>How long an AI category recommendation is reused for the same shop + title.</summary>
    private static readonly TimeSpan CategoryRecommendCacheTtl = TimeSpan.FromMinutes(10);

    public ListingAutoPublishOrchestrator(
        IApplicationDbContext db,
        ISettingProvider settings,
        IListingSyncService listingSync,
        ICategoryService categoryService,
        IListingDescriptionAiService rewrite,
        IListingSpecificsMapService specifics,
        IListingAutoPublishPrepService prep,
        IListingAutoPublishStepCheckpointService checkpoints,
        IListingSellerSyncLease sellerLease,
        IListingAutoPublishCancellationCoordinator cancellation,
        IMemoryCache cache,
        ILogger<ListingAutoPublishOrchestrator> logger)
    {
        _db = db;
        _settings = settings;
        _listingSync = listingSync;
        _categoryService = categoryService;
        _rewrite = rewrite;
        _specifics = specifics;
        _prep = prep;
        _checkpoints = checkpoints;
        _sellerLease = sellerLease;
        _cancellation = cancellation;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Guid> RunAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        var ctx = await LoadContextAsync(listingId, cancellationToken);
        await EnsurePreGatesAsync(ctx, cancellationToken);
        var shop = ctx.Shop!;

        if (!_sellerLease.TryBegin(ctx.Listing.OwnerId, ListingSyncLeaseSource.AutoPublish, out var sellerLease))
        {
            throw new BusinessException("Seller đang sync listing — thử lại sau.");
        }

        try
        {
            var run = new Domain.Entities.ListingAutoPublishRun
            {
                ListingId = listingId,
                ShopId = shop.Id,
                OwnerId = ctx.Listing.OwnerId,
                Status = ListingAutoPublishRunStatuses.Running,
                StartedAt = DateTime.UtcNow
            };
            _db.ListingAutoPublishRuns.Add(run);
            await _db.SaveChangesAsync(cancellationToken);

            var linkedCt = _cancellation.LinkRun(run.Id, shop.Id, cancellationToken);
            var steps = new List<ListingAutoPublishStepDto>();
            var completedSteps = await _checkpoints.EnsureLoadedAsync(listingId, linkedCt);
            var swTotal = Stopwatch.StartNew();
            var publishSucceeded = false;

            try
            {
                linkedCt.ThrowIfCancellationRequested();

                var detail = await _listingSync.GetByIdAsync(listingId, refreshFromFourSeller: false, linkedCt);

            await RunStepAsync(run, listingId, completedSteps, steps, ListingAutoPublishStepCodes.LoadFull, "B1: Lấy draft full", linkedCt,
                async step =>
                {
                    var subSteps = new List<ListingAutoPublishSubStepDto>
                    {
                        new() { Code = "load_from_db", Success = true }
                    };
                    // Attach up front so a throw below still surfaces the sub-step detail in the log.
                    step.SubSteps = subSteps;

                    // Always re-fetch from 4Seller so retry/fail recovery does not trust a stale local mirror.
                    var syncSubStep = new ListingAutoPublishSubStepDto { Code = "sync_4seller", Success = false };
                    subSteps.Add(syncSubStep);
                    try
                    {
                        detail = await _listingSync.GetByIdAsync(listingId, refreshFromFourSeller: true, linkedCt);
                        if (!IsMirrorSufficient(detail))
                        {
                            await _listingSync.SyncOneAsync(listingId, linkedCt);
                            detail = await _listingSync.GetByIdAsync(listingId, refreshFromFourSeller: true, linkedCt);
                        }

                        var sufficient = IsMirrorSufficient(detail);
                        syncSubStep.Success = sufficient;
                        if (!sufficient)
                        {
                            throw new BusinessException("Mirror thiếu dữ liệu sau sync 4Seller.");
                        }
                    }
                    catch (Exception ex) when (ex is not BusinessException)
                    {
                        syncSubStep.Success = false;
                        syncSubStep.Error = ex.Message;
                        throw new BusinessException($"Sync 4Seller thất bại: {ex.Message}");
                    }
                });

            linkedCt.ThrowIfCancellationRequested();

            await RunStepAsync(run, listingId, completedSteps, steps, ListingAutoPublishStepCodes.Category, "B2: Category AI", linkedCt, async step =>
            {
                var subSteps = new List<ListingAutoPublishSubStepDto>();
                step.SubSteps = subSteps;

                var site = string.IsNullOrWhiteSpace(detail.Site) ? "US" : detail.Site!;
                var title = detail.ProductName?.Trim() ?? string.Empty;
                var currentCategoryId = detail.CategoryId?.Trim();
                var hasCurrentCategory = !string.IsNullOrWhiteSpace(currentCategoryId);

                var verifyEnabled = await ReadCategoryVerifyEnabledAsync(linkedCt);
                if (!verifyEnabled && hasCurrentCategory)
                {
                    var remapped = await TryRemapBlockedCurrentCategoryAsync(
                        listingId,
                        detail,
                        site,
                        currentCategoryId!,
                        subSteps,
                        completedSteps,
                        linkedCt);
                    if (remapped is not null)
                    {
                        detail = remapped;
                        return;
                    }

                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = ListingAutoPublishStepCodes.CategorySubSteps.Kept,
                        Success = true,
                        Skipped = true,
                        Note = "Đối chiếu AI đang tắt trong Settings — giữ category hiện tại."
                    });
                    return;
                }

                if (title.Length == 0)
                {
                    if (!hasCurrentCategory)
                    {
                        throw new BusinessException("Listing chưa có title để AI gợi ý category.");
                    }

                    var remappedNoTitle = await TryRemapBlockedCurrentCategoryAsync(
                        listingId,
                        detail,
                        site,
                        currentCategoryId!,
                        subSteps,
                        completedSteps,
                        linkedCt);
                    if (remappedNoTitle is not null)
                    {
                        detail = remappedNoTitle;
                        return;
                    }

                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = ListingAutoPublishStepCodes.CategorySubSteps.Kept,
                        Success = true,
                        Skipped = true,
                        Note = "Không có title để đối chiếu — giữ category hiện tại."
                    });
                    return;
                }

                subSteps.Add(new ListingAutoPublishSubStepDto
                {
                    Code = ListingAutoPublishStepCodes.CategorySubSteps.AiRequest,
                    Success = true,
                    Note = $"site={site}; shopId={detail.FourSellerShopId}; title={Truncate(title, 120)}"
                });

                RecommendCategoryResultDto recommend;
                try
                {
                    recommend = await RecommendCategoryCachedAsync(detail, site, title, linkedCt);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 4Seller AI is a best-effort cross-check: only fail the run when the listing has
                    // no category at all, since Specifics cannot be mapped without one.
                    if (!hasCurrentCategory)
                    {
                        throw;
                    }

                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = ListingAutoPublishStepCodes.CategorySubSteps.AiFailed,
                        Success = false,
                        Error = ex.Message,
                        Note = $"Giữ category hiện tại {currentCategoryId} — {detail.CategoryName}."
                    });

                    var remappedAfterAiFail = await TryRemapBlockedCurrentCategoryAsync(
                        listingId,
                        detail,
                        site,
                        currentCategoryId!,
                        subSteps,
                        completedSteps,
                        linkedCt);
                    if (remappedAfterAiFail is not null)
                    {
                        detail = remappedAfterAiFail;
                    }

                    return;
                }

                if (!string.IsNullOrWhiteSpace(recommend.ReplacedFromCategoryId))
                {
                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = ListingAutoPublishStepCodes.CategorySubSteps.BlockedRemapped,
                        Success = true,
                        Note = recommend.ReplacementReason
                               ?? $"{recommend.ReplacedFromCategoryId} → {recommend.CategoryId}"
                    });
                }

                subSteps.Add(new ListingAutoPublishSubStepDto
                {
                    Code = ListingAutoPublishStepCodes.CategorySubSteps.AiResponse,
                    Success = true,
                    Note = $"{recommend.CategoryId} — {recommend.CategoryName}; "
                           + $"path={recommend.NodePath ?? recommend.CategoryPath ?? "?"}; "
                           + $"leaf={recommend.IsLeaf}; validated={recommend.IsValidated}"
                });

                if (!recommend.IsValidated)
                {
                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = ListingAutoPublishStepCodes.CategorySubSteps.AiInvalid,
                        Success = false,
                        Error = recommend.ValidationWarning
                    });
                }

                var options = await ReadCategoryGuardrailOptionsAsync(linkedCt);
                var currentInfo = await BuildCurrentCategoryInfoAsync(
                    site,
                    currentCategoryId,
                    detail.CategoryName,
                    linkedCt);
                var aiInfo = new ListingCategoryInfo(
                    recommend.CategoryId,
                    recommend.CategoryName,
                    recommend.CategoryPath);
                var decision = ListingCategoryGuardrail.Decide(
                    currentInfo,
                    aiInfo,
                    recommend.IsValidated,
                    title,
                    options);

                _logger.LogInformation(
                    "Auto publish category decision for listing {ListingId}: {Kind} — current={CurrentCategoryId} "
                    + "({CurrentCategoryName}), ai={AiCategoryId} ({AiCategoryName}), reason={Reason}",
                    listingId,
                    decision.Kind,
                    currentInfo.CategoryId,
                    currentInfo.CategoryName,
                    aiInfo.CategoryId,
                    aiInfo.CategoryName,
                    decision.Reason);

                if (decision.Kind is ListingCategoryDecisionKind.Confirmed or ListingCategoryDecisionKind.Keep)
                {
                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = decision.Kind == ListingCategoryDecisionKind.Confirmed
                            ? ListingAutoPublishStepCodes.CategorySubSteps.Confirmed
                            : ListingAutoPublishStepCodes.CategorySubSteps.Kept,
                        Success = true,
                        Note = decision.Reason
                    });

                    // Safety net: Confirmed/Keep on a blocked leaf (remap of AI failed) still must leave it.
                    if (!string.IsNullOrWhiteSpace(detail.CategoryId))
                    {
                        var remappedKeep = await TryRemapBlockedCurrentCategoryAsync(
                            listingId,
                            detail,
                            site,
                            detail.CategoryId!,
                            subSteps,
                            completedSteps,
                            linkedCt);
                        if (remappedKeep is not null)
                        {
                            detail = remappedKeep;
                        }
                    }

                    return;
                }

                var isOverride = decision.Kind == ListingCategoryDecisionKind.Override;
                detail = await ApplyRecommendedCategoryAsync(
                    listingId,
                    detail,
                    recommend,
                    clearStaleAttributes: isOverride,
                    linkedCt);

                subSteps.Add(new ListingAutoPublishSubStepDto
                {
                    Code = isOverride
                        ? ListingAutoPublishStepCodes.CategorySubSteps.Overridden
                        : ListingAutoPublishStepCodes.CategorySubSteps.Assigned,
                    Success = true,
                    Note = isOverride
                        ? $"{currentCategoryId} ({currentInfo.CategoryName}) → {recommend.CategoryId} "
                          + $"({recommend.CategoryName}). {decision.Reason}"
                        : $"{recommend.CategoryId} ({recommend.CategoryName}). {decision.Reason}"
                });

                if (!isOverride)
                {
                    return;
                }

                // Specifics mapped against the old category are meaningless now; drop the checkpoint
                // (and the in-memory copy RunStepAsync reads) so the step maps against the new schema.
                await _checkpoints.InvalidateFromAsync(
                    listingId,
                    ListingAutoPublishStepCodes.Specifics,
                    CancellationToken.None);
                foreach (var code in ListingAutoPublishStepCodes.PipelineOrder
                    .SkipWhile(code => !string.Equals(code, ListingAutoPublishStepCodes.Specifics, StringComparison.OrdinalIgnoreCase)))
                {
                    completedSteps.Remove(code);
                }

                subSteps.Add(new ListingAutoPublishSubStepDto
                {
                    Code = ListingAutoPublishStepCodes.CategorySubSteps.AttributesReset,
                    Success = true,
                    Note = "Đã xoá Specifics của category cũ và reset checkpoint để map lại."
                });
            });

            linkedCt.ThrowIfCancellationRequested();

            await RunStepAsync(run, listingId, completedSteps, steps, ListingAutoPublishStepCodes.Commerce, "B3+B4+SKU", linkedCt, async _ =>
            {
                await _prep.ApplyDefaultPricingAsync(detail.OwnerId, detail, linkedCt);
                _prep.ApplyDefaultQuantity(detail);
                _prep.GenerateBatchSkus(detail);
                detail = await _listingSync.UpdateAsync(
                    listingId,
                    ListingAutoPublishMirrorMapper.ToUpdateDto(detail),
                    linkedCt);
            });

            linkedCt.ThrowIfCancellationRequested();

            await RunStepAsync(run, listingId, completedSteps, steps, ListingAutoPublishStepCodes.Specifics, "Map Specifics", linkedCt, async step =>
            {
                if (string.IsNullOrWhiteSpace(detail.CategoryId))
                {
                    throw new BusinessException("Thiếu category trước khi map Specifics.");
                }

                var mapResult = await _specifics.MapAsync(
                    listingId,
                    new MapListingSpecificsRequest
                    {
                        CategoryId = detail.CategoryId!,
                        CategoryName = detail.CategoryName,
                        ShopId = detail.ShopId,
                        Title = detail.ProductName,
                        ExistingAttributes = detail.Attributes.Select(a => new UpdateListingMirrorAttributeDto
                        {
                            AttrId = a.AttrId,
                            AttrName = a.AttrName,
                            AttrValue = a.AttrValue,
                            ValueId = a.ValueId,
                            ValueType = a.ValueType,
                            IsRequired = a.IsRequired,
                            Values = a.Values?.Select(v => new UpdateListingMirrorAttributeValueDto
                            {
                                ValueId = v.ValueId,
                                ValueName = v.ValueName
                            }).ToList()
                        }).ToList()
                    },
                    linkedCt);

                if (mapResult.ParsedPairCount == 0 && mapResult.Matched.Count == 0)
                {
                    step.Skipped = true;
                    step.SubSteps =
                    [
                        new ListingAutoPublishSubStepDto
                        {
                            Code = "no_key_value_pairs",
                            Success = true,
                            Skipped = true,
                            Note = "Mô tả không còn dòng Key: Value và category này không bắt buộc Specifics nào."
                        }
                    ];
                    return;
                }

                var seededRequired = mapResult.Matched
                    .Where(m => string.Equals(m.MatchMethod, "RequiredDefault", StringComparison.Ordinal))
                    .Select(m => $"{m.AttrName} = {m.AttrValue}")
                    .ToList();
                if (seededRequired.Count > 0)
                {
                    (step.SubSteps ??= []).Add(new ListingAutoPublishSubStepDto
                    {
                        Code = "required_defaults",
                        Success = true,
                        Note = "Điền mặc định cho attribute bắt buộc của category: "
                               + string.Join(", ", seededRequired)
                    });
                }

                // Mandatory rows the category exposes no value for (free-text) cannot be invented here.
                // Stopping now names the attribute, where 4Seller would only answer
                // 【Publish Failed】Specifics.<attribute name> two steps later.
                if (mapResult.MissingRequired.Count > 0)
                {
                    throw new BusinessException(
                        "Category yêu cầu Specifics mà không map được từ mô tả và cũng không có giá trị "
                        + "mặc định hợp lệ: "
                        + string.Join(
                            ", ",
                            mapResult.MissingRequired.Select(m => $"{m.AttrName} (#{m.AttrId})")));
                }

                foreach (var m in mapResult.Matched.Where(m =>
                    !m.SkippedExisting
                    && !ListingSpecificsMappingEngine.IsAutoPublishBlockedSpecificAttribute(m.AttrName)))
                {
                    var existing = detail.Attributes.FirstOrDefault(a =>
                        string.Equals(a.AttrId, m.AttrId, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        detail.Attributes.Add(new ListingAttributeDto
                        {
                            AttrId = m.AttrId,
                            AttrName = m.AttrName,
                            AttrValue = m.AttrValue,
                            ValueId = m.ValueId,
                            IsRequired = m.IsRequired,
                            Values = m.Values.Select(v => new ListingAttributeValueDto
                            {
                                ValueId = v.ValueId,
                                ValueName = v.ValueName
                            }).ToList()
                        });
                    }
                    else
                    {
                        existing.AttrValue = m.AttrValue;
                        existing.ValueId = m.ValueId;
                        existing.Values = m.Values.Select(v => new ListingAttributeValueDto
                        {
                            ValueId = v.ValueId,
                            ValueName = v.ValueName
                        }).ToList();
                    }
                }

                detail = await _listingSync.UpdateAsync(
                    listingId,
                    ListingAutoPublishMirrorMapper.ToUpdateDto(detail),
                    linkedCt);
            });

            linkedCt.ThrowIfCancellationRequested();

            await RunStepAsync(run, listingId, completedSteps, steps, ListingAutoPublishStepCodes.Rewrite, "B5: Rewrite", linkedCt, async step =>
            {
                if (detail.DescriptionRewritten)
                {
                    step.Skipped = true;
                    step.SubSteps =
                    [
                        new ListingAutoPublishSubStepDto
                        {
                            Code = "already_rewritten",
                            Success = true,
                            Skipped = true
                        }
                    ];
                    return;
                }

                var rewriteResult = await _rewrite.RewriteAsync(
                    new RewriteListingDescriptionRequest
                    {
                        Title = detail.ProductName ?? "",
                        Description = detail.DescriptionHtml ?? "",
                        CategoryName = detail.CategoryName,
                        BrandName = detail.BrandName,
                        Attributes = detail.Attributes.Select(a => new RewriteListingAttributeDto
                        {
                            Name = a.AttrName,
                            Value = a.AttrValue
                        }).ToList()
                    },
                    linkedCt);

                ListingAutoPublishMirrorMapper.ApplyRewriteResult(detail, rewriteResult);
                detail = await _listingSync.UpdateAsync(
                    listingId,
                    ListingAutoPublishMirrorMapper.ToUpdateDto(detail),
                    linkedCt);
            });

            linkedCt.ThrowIfCancellationRequested();

            await RunStepAsync(run, listingId, completedSteps, steps, ListingAutoPublishStepCodes.Media, "B6+B7+B8 Media", linkedCt, async step =>
            {
                // Same source the pre-gate validated. detail.SizeChartCaptureUrl is derived data
                // that a mirror update round-trip can drop, so it is only a fallback here.
                var captureUrl = !string.IsNullOrWhiteSpace(ctx.SizeChartImageUrl)
                    ? ctx.SizeChartImageUrl
                    : detail.SizeChartCaptureUrl;

                var swUpload = Stopwatch.StartNew();
                if (!string.IsNullOrWhiteSpace(captureUrl))
                {
                    await _prep.UploadSizeChartFromCaptureAsync(
                        listingId,
                        detail,
                        captureUrl!,
                        linkedCt);
                }

                var uploadMs = swUpload.ElapsedMilliseconds;
                var swPrep = Stopwatch.StartNew();
                _prep.PrepareImages(detail, captureUrl);
                var prepMs = swPrep.ElapsedMilliseconds;

                var swUpdate = Stopwatch.StartNew();
                detail = await _listingSync.UpdateAsync(
                    listingId,
                    ListingAutoPublishMirrorMapper.ToUpdateDto(detail),
                    linkedCt);
                var updateMs = swUpdate.ElapsedMilliseconds;

                step.SubSteps =
                [
                    new ListingAutoPublishSubStepDto
                    {
                        Code = "size_chart_upload",
                        Success = true,
                        Skipped = string.IsNullOrWhiteSpace(captureUrl),
                        Error = $"uploadMs={uploadMs}"
                    },
                    new ListingAutoPublishSubStepDto
                    {
                        Code = "prepare_images",
                        Success = true,
                        Error = $"prepMs={prepMs}"
                    },
                    new ListingAutoPublishSubStepDto
                    {
                        Code = "persist_local",
                        Success = true,
                        Error = $"updateMs={updateMs}"
                    }
                ];
            });

            linkedCt.ThrowIfCancellationRequested();

            await RunStepAsync(run, listingId, completedSteps, steps, ListingAutoPublishStepCodes.Validate, "Kiểm tra cuối", linkedCt, async step =>
            {
                var subSteps = new List<ListingAutoPublishSubStepDto>();
                step.SubSteps = subSteps;

                var hadShipping = !_prep.NeedsShippingAutofill(detail);
                try
                {
                    var shippingChanged = await _prep.ApplyDefaultShippingAsync(detail.OwnerId, detail, linkedCt);
                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = "shipping_template",
                        Success = _prep.IsShippingComplete(detail),
                        Skipped = hadShipping && !shippingChanged
                    });
                }
                catch (Exception ex)
                {
                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = "shipping_template",
                        Success = false,
                        Error = ex.Message
                    });
                }

                var hadWarehouse = !string.IsNullOrWhiteSpace(detail.WarehouseId);
                try
                {
                    if (detail.ShopId is null)
                    {
                        subSteps.Add(new ListingAutoPublishSubStepDto
                        {
                            Code = "warehouse",
                            Success = false,
                            Error = "Thiếu shopId."
                        });
                    }
                    else
                    {
                        var warehouseChanged = await _prep.ApplyDefaultWarehouseAsync(listingId, detail, linkedCt);
                        subSteps.Add(new ListingAutoPublishSubStepDto
                        {
                            Code = "warehouse",
                            Success = !string.IsNullOrWhiteSpace(detail.WarehouseId),
                            Skipped = hadWarehouse && !warehouseChanged,
                            Error = string.IsNullOrWhiteSpace(detail.WarehouseId)
                                ? "Shop chưa có warehouse mặc định từ 4Seller."
                                : null
                        });
                    }
                }
                catch (Exception ex)
                {
                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = "warehouse",
                        Success = false,
                        Error = ex.Message
                    });
                }

                subSteps.Add(new ListingAutoPublishSubStepDto
                {
                    Code = "package_weight",
                    Success = detail.PackageWeight is > 0
                });
                subSteps.Add(new ListingAutoPublishSubStepDto
                {
                    Code = "package_dims",
                    Success = detail.PackageLength is > 0
                               && detail.PackageWidth is > 0
                               && detail.PackageHeight is > 0
                });
                subSteps.Add(new ListingAutoPublishSubStepDto
                {
                    Code = "size_chart",
                    Success = _prep.HasSizeChartInGallery(detail)
                });
                subSteps.Add(new ListingAutoPublishSubStepDto
                {
                    Code = "raw_payload",
                    Success = detail.HasRawPayload
                });

                _prep.EnsureUniqueSellerSkus(detail);
                subSteps.Add(new ListingAutoPublishSubStepDto
                {
                    Code = "seller_sku_unique",
                    Success = _prep.HasUniqueSellerSkus(detail)
                });

                // Re-apply default QTY even when commerce was checkpointed — inbound sync used to
                // SUM multi-row stockInfoList and leave inflated Quantity on resume.
                _prep.ApplyDefaultQuantity(detail);

                var failed = subSteps.FirstOrDefault(s => !s.Success && !s.Skipped);
                if (failed is not null)
                {
                    throw new BusinessException(
                        failed.Error ?? $"Kiểm tra thất bại: {failed.Code}");
                }

                // Snapshot full prep state locally before 4Seller publish (reviewable in edit page).
                try
                {
                    detail = await _listingSync.UpdateAsync(
                        listingId,
                        ListingAutoPublishMirrorMapper.ToUpdateDto(detail),
                        linkedCt);
                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = "persist_local",
                        Success = true
                    });
                }
                catch (Exception ex)
                {
                    subSteps.Add(new ListingAutoPublishSubStepDto
                    {
                        Code = "persist_local",
                        Success = false,
                        Error = ex.Message
                    });
                    throw new BusinessException($"Không lưu được listing local: {ex.Message}");
                }
            });

            linkedCt.ThrowIfCancellationRequested();

            await RunStepAsync(run, listingId, completedSteps, steps, ListingAutoPublishStepCodes.Publish, "Publish 4Seller", linkedCt, async _ =>
            {
                await EnsurePublishGatesAsync(listingId, linkedCt);

                _prep.EnsureUniqueSellerSkus(detail);

                await _listingSync.PublishAsync(
                    listingId,
                    ListingAutoPublishMirrorMapper.ToUpdateDto(detail),
                    ListingPublishSources.Auto,
                    linkedCt);

                // 4Seller accepted the listing — from here on the run is a success even if a
                // cancellation lands while finalising local bookkeeping.
                publishSucceeded = true;

                var listing = await _db.Listings.FirstAsync(l => l.Id == listingId, CancellationToken.None);
                listing.LastAutoPublishRunId = run.Id;
                listing.UpdatedAt = DateTime.UtcNow;
                await ApplyPublishLimitPublishedIncrementAsync(shop.Id, CancellationToken.None);
                await _db.SaveChangesAsync(CancellationToken.None);
            });

            await ClearCheckpointsAsync(listingId);

            run.Status = ListingAutoPublishRunStatuses.Success;
            run.ErrorMessage = null;
            run.FailedStepCode = null;
        }
        catch (OperationCanceledException) when (linkedCt.IsCancellationRequested && publishSucceeded)
        {
            run.Status = ListingAutoPublishRunStatuses.Success;
            run.ErrorMessage = null;
            run.FailedStepCode = null;
            await ClearCheckpointsAsync(listingId);
        }
        catch (OperationCanceledException) when (linkedCt.IsCancellationRequested)
        {
            run.Status = ListingAutoPublishRunStatuses.Cancelled;
            run.ErrorMessage = "Auto publish đã bị tắt.";
            run.FailedStepCode = steps.LastOrDefault(s => !s.Success)?.Code;
        }
        catch (BusinessException ex)
        {
            run.Status = ListingAutoPublishRunStatuses.Failed;
            run.ErrorMessage = ex.Message;
            run.FailedStepCode = steps.LastOrDefault(s => !s.Success)?.Code ?? steps.LastOrDefault()?.Code;
            // Full rerun next pick/retry — stale checkpoints would skip load_full and keep bad mirror data.
            await ClearCheckpointsAsync(listingId);

            if (ListingPublishLimitErrors.IsDayLimit(ex.Message))
            {
                await BlockShopForDayLimitAsync(shop.Id, CancellationToken.None);
            }
            else if (ShouldSkipListingPermanently(ex))
            {
                await MarkListingSkippedAsync(listingId, ex.Message, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto publish failed for listing {ListingId}", listingId);
            run.Status = ListingAutoPublishRunStatuses.Failed;
            run.ErrorMessage = ex.Message;
            run.FailedStepCode = steps.LastOrDefault(s => !s.Success)?.Code ?? steps.LastOrDefault()?.Code;
            await ClearCheckpointsAsync(listingId);
            if (ListingPublishLimitErrors.IsDayLimit(ex.Message))
            {
                await BlockShopForDayLimitAsync(shop.Id, CancellationToken.None);
            }
            else if (ShouldSkipListingPermanently(ex))
            {
                await MarkListingSkippedAsync(listingId, ex.Message, CancellationToken.None);
            }
        }
            finally
            {
                swTotal.Stop();
                run.FinishedAt = DateTime.UtcNow;
                run.DurationMs = (int)Math.Min(int.MaxValue, swTotal.ElapsedMilliseconds);
                foreach (var step in steps)
                {
                    step.InProgress = false;
                }

                run.StepsJson = JsonSerializer.Serialize(steps, JsonOpts);

                await PersistRunOutcomeAsync(run, listingId);
                _cancellation.UnregisterRun(run.Id, shop.Id);
            }

            return run.Id;
        }
        finally
        {
            sellerLease.Dispose();
        }
    }

    private async Task<bool> ReadCategoryVerifyEnabledAsync(CancellationToken ct)
    {
        var raw = await _settings.GetValueAsync(ListingAutoPublishSettingKeys.CategoryVerifyEnabled, ct);
        return string.IsNullOrWhiteSpace(raw)
            ? string.Equals(
                ListingAutoPublishSettingKeys.DefaultCategoryVerifyEnabled,
                "true",
                StringComparison.OrdinalIgnoreCase)
            : string.Equals(raw.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ListingCategoryGuardrail.Options> ReadCategoryGuardrailOptionsAsync(CancellationToken ct)
    {
        var values = await _settings.GetValuesAsync(
            [
                ListingAutoPublishSettingKeys.CategoryRiskyAncestorIds,
                ListingAutoPublishSettingKeys.CategoryRiskyNameKeywords,
                ListingAutoPublishSettingKeys.CategoryRiskyTitleKeywords,
                ListingAutoPublishSettingKeys.CategoryBlockedRemap
            ],
            ct);

        return ListingCategoryGuardrail.ParseOptions(
            Fallback(values, ListingAutoPublishSettingKeys.CategoryRiskyAncestorIds, ListingAutoPublishSettingKeys.DefaultCategoryRiskyAncestorIds),
            Fallback(values, ListingAutoPublishSettingKeys.CategoryRiskyNameKeywords, ListingAutoPublishSettingKeys.DefaultCategoryRiskyNameKeywords),
            Fallback(values, ListingAutoPublishSettingKeys.CategoryRiskyTitleKeywords, ListingAutoPublishSettingKeys.DefaultCategoryRiskyTitleKeywords),
            Fallback(values, ListingAutoPublishSettingKeys.CategoryBlockedRemap, ListingAutoPublishSettingKeys.DefaultCategoryBlockedRemap));

        static string Fallback(IReadOnlyDictionary<string, string> values, string key, string fallback)
        {
            var value = values.GetValueOrDefault(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }

    /// <summary>
    /// Enriches the category already on the listing with its catalog path so the guardrail can test
    /// ancestors, not just the display name.
    /// </summary>
    private async Task<ListingCategoryInfo> BuildCurrentCategoryInfoAsync(
        string site,
        string? categoryId,
        string? categoryName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return new ListingCategoryInfo(null, categoryName, null);
        }

        var catalog = await _categoryService.GetByExternalIdAsync(site, categoryId, ct);
        return new ListingCategoryInfo(
            categoryId,
            string.IsNullOrWhiteSpace(categoryName) ? catalog?.Name : categoryName,
            catalog?.Path);
    }

    /// <summary>
    /// When the listing already sits on a blocked leaf (Settings <c>category_blocked_remap</c>),
    /// swaps it for the replacement sibling and resets Specifics — used when AI is off / failed /
    /// or Confirmed/Keep would otherwise leave the blocked leaf in place.
    /// </summary>
    /// <returns>Updated detail when remapped; <c>null</c> when no remap applied.</returns>
    private async Task<ListingDetailDto?> TryRemapBlockedCurrentCategoryAsync(
        Guid listingId,
        ListingDetailDto detail,
        string site,
        string currentCategoryId,
        List<ListingAutoPublishSubStepDto> subSteps,
        HashSet<string> completedSteps,
        CancellationToken ct)
    {
        var values = await _settings.GetValuesAsync(
            [ListingAutoPublishSettingKeys.CategoryBlockedRemap],
            ct);
        var raw = values.GetValueOrDefault(ListingAutoPublishSettingKeys.CategoryBlockedRemap);
        var map = ListingCategoryBlockedRemap.Parse(
            string.IsNullOrWhiteSpace(raw)
                ? ListingAutoPublishSettingKeys.DefaultCategoryBlockedRemap
                : raw);

        if (!ListingCategoryBlockedRemap.TryResolve(map, currentCategoryId, out var replacementId))
        {
            return null;
        }

        var replacement = await _categoryService.GetByExternalIdAsync(site, replacementId, ct);
        if (replacement is null || !replacement.IsActive || !replacement.IsLeaf)
        {
            subSteps.Add(new ListingAutoPublishSubStepDto
            {
                Code = ListingAutoPublishStepCodes.CategorySubSteps.BlockedRemapped,
                Success = false,
                Error =
                    $"Category {currentCategoryId} thuộc blocked remap → {replacementId}, nhưng replacement không hợp lệ trong catalog."
            });
            return null;
        }

        var fromName = detail.CategoryName;
        var pathIds = (replacement.Path ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s != "0")
            .ToList();
        if (pathIds.Count == 0
            || !string.Equals(pathIds[^1], replacement.ExternalId, StringComparison.OrdinalIgnoreCase))
        {
            pathIds.Add(replacement.ExternalId);
        }

        var recommend = new RecommendCategoryResultDto
        {
            CategoryId = replacement.ExternalId,
            CategoryName = replacement.Name,
            CategoryPath = replacement.Path,
            PathIds = pathIds,
            IsLeaf = true,
            IsValidated = true,
            Site = site,
            ReplacedFromCategoryId = currentCategoryId,
            ReplacedFromCategoryName = fromName,
            ReplacementReason =
                $"Category hiện tại {currentCategoryId} ({fromName}) thuộc blocked remap — chuyển sang {replacement.ExternalId} ({replacement.Name})."
        };

        detail = await ApplyRecommendedCategoryAsync(
            listingId,
            detail,
            recommend,
            clearStaleAttributes: true,
            ct);

        subSteps.Add(new ListingAutoPublishSubStepDto
        {
            Code = ListingAutoPublishStepCodes.CategorySubSteps.BlockedRemapped,
            Success = true,
            Note = recommend.ReplacementReason
        });

        await _checkpoints.InvalidateFromAsync(
            listingId,
            ListingAutoPublishStepCodes.Specifics,
            CancellationToken.None);
        foreach (var code in ListingAutoPublishStepCodes.PipelineOrder
            .SkipWhile(code => !string.Equals(code, ListingAutoPublishStepCodes.Specifics, StringComparison.OrdinalIgnoreCase)))
        {
            completedSteps.Remove(code);
        }

        subSteps.Add(new ListingAutoPublishSubStepDto
        {
            Code = ListingAutoPublishStepCodes.CategorySubSteps.AttributesReset,
            Success = true,
            Note = "Đã xoá Specifics của category bị chặn và reset checkpoint để map lại."
        });

        return detail;
    }

    /// <summary>
    /// Caches the AI recommendation per site + title so a retried run — or the same scraped product
    /// published into several shops — does not hit 4Seller again. The shop is deliberately out of the
    /// key: 4Seller answers the same category for a title regardless of <c>shopId</c> (measured), and
    /// the answer is cross-checked against our own catalog before it is used.
    /// </summary>
    private async Task<RecommendCategoryResultDto> RecommendCategoryCachedAsync(
        ListingDetailDto detail,
        string site,
        string title,
        CancellationToken ct)
    {
        var cacheKey = $"autopublish:category:{site}:{title.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out RecommendCategoryResultDto? cached) && cached is not null)
        {
            return cached;
        }

        var recommend = await _categoryService.RecommendCategoryAsync(
            detail.OwnerId,
            new RecommendCategoryRequest
            {
                Site = site,
                Title = title,
                OwnerUserId = detail.OwnerId,
                FourSellerShopId = detail.FourSellerShopId
            },
            ct,
            allowOwnerOverride: true);

        // Do not cache an unvalidated answer — a later run may succeed after catalog sync / remap fix.
        if (recommend.IsValidated)
        {
            _cache.Set(cacheKey, recommend, CategoryRecommendCacheTtl);
        }

        return recommend;
    }

    /// <summary>
    /// Puts the recommended category on the listing: bind through 4Seller recent-use when available,
    /// otherwise mirror-only (the push payload carries <c>categoryId</c> either way).
    /// </summary>
    private async Task<ListingDetailDto> ApplyRecommendedCategoryAsync(
        Guid listingId,
        ListingDetailDto detail,
        RecommendCategoryResultDto recommend,
        bool clearStaleAttributes,
        CancellationToken ct)
    {
        var recent = await _listingSync.GetRecentCategoriesAsync(
            listingId,
            detail.FourSellerShopId,
            detail.Site,
            ct);
        var match = recent.FirstOrDefault(r =>
            string.Equals(r.CategoryId, recommend.CategoryId, StringComparison.OrdinalIgnoreCase));

        if (match is not null && match.Id > 0)
        {
            await _listingSync.BindCategoryAsync(
                listingId,
                new BindListingCategoryRequest
                {
                    RecentCategoryId = match.Id,
                    CategoryId = recommend.CategoryId,
                    CategoryName = recommend.CategoryName,
                    NodePathId = match.NodePathId
                },
                ct);

            // BindCategoryAsync writes straight to the mirror, so re-read to pick it up.
            detail = await _listingSync.GetByIdAsync(listingId, false, ct);
            if (!clearStaleAttributes)
            {
                return detail;
            }

            detail.Attributes.Clear();
            return await _listingSync.UpdateAsync(
                listingId,
                ListingAutoPublishMirrorMapper.ToUpdateDto(detail),
                ct);
        }

        detail.CategoryId = recommend.CategoryId;
        detail.CategoryName = recommend.CategoryName;
        if (clearStaleAttributes)
        {
            detail.Attributes.Clear();
        }

        return await _listingSync.UpdateAsync(
            listingId,
            ListingAutoPublishMirrorMapper.ToUpdateDto(detail),
            ct);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    /// <summary>
    /// Clears checkpoints after a successful publish. A failure here must not turn an accepted
    /// publish into a thrown run — leftover checkpoints self-heal on the next
    /// <see cref="IListingAutoPublishStepCheckpointService.EnsureLoadedAsync"/> once the listing
    /// has left draft.
    /// </summary>
    private async Task ClearCheckpointsAsync(Guid listingId)
    {
        try
        {
            await _checkpoints.ClearAsync(listingId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto publish checkpoint clear failed for listing {ListingId}", listingId);
        }
    }

    /// <summary>
    /// Writes the terminal run state. When the pipeline failed inside a save, the change tracker
    /// still holds the rejected entities and a plain SaveChanges would throw again, leaving the run
    /// stuck at "running" until stale recovery. Retry once on a cleared tracker so the log survives.
    /// </summary>
    private async Task PersistRunOutcomeAsync(Domain.Entities.ListingAutoPublishRun run, Guid listingId)
    {
        try
        {
            var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, CancellationToken.None);
            if (listing is not null)
            {
                listing.LastAutoPublishRunId = run.Id;
                listing.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto publish run {RunId} outcome save failed — retrying clean", run.Id);
        }

        try
        {
            _db.ChangeTracker.Clear();
            _db.ListingAutoPublishRuns.Update(run);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto publish run {RunId} outcome lost — run stays stale", run.Id);
        }
    }

    private async Task RunStepAsync(
        Domain.Entities.ListingAutoPublishRun run,
        Guid listingId,
        HashSet<string> completedSteps,
        List<ListingAutoPublishStepDto> steps,
        string code,
        string label,
        CancellationToken ct,
        Func<ListingAutoPublishStepDto, Task> action)
    {
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        var step = new ListingAutoPublishStepDto { Code = code, Label = label };

        if (completedSteps.Contains(code))
        {
            step.Success = true;
            step.Skipped = true;
            step.DurationMs = 0;
            steps.Add(step);
            await PersistStepsProgressAsync(run, steps);
            return;
        }

        step.InProgress = true;
        steps.Add(step);
        await PersistStepsProgressAsync(run, steps);

        try
        {
            await action(step);
            step.Success = true;
            step.InProgress = false;
        }
        catch (Exception ex)
        {
            step.Success = false;
            step.InProgress = false;
            step.Error = ex.Message;
            step.DurationMs = (int)sw.ElapsedMilliseconds;
            await PersistStepsProgressAsync(run, steps);
            throw;
        }

        step.DurationMs = (int)sw.ElapsedMilliseconds;
        completedSteps.Add(code);

        // Checkpoint persistence finalises work already done — cancelling here would lose the
        // resume point and force the next run to redo an expensive step.
        await _checkpoints.MarkCompletedAsync(listingId, code, CancellationToken.None);
        await PersistStepsProgressAsync(run, steps);
    }

    /// <summary>
    /// Flushes the live step timeline while status is still Running so the modal can show progress.
    /// Failures here must not abort the pipeline — the terminal finally block rewrites StepsJson.
    /// </summary>
    private async Task PersistStepsProgressAsync(
        Domain.Entities.ListingAutoPublishRun run,
        List<ListingAutoPublishStepDto> steps)
    {
        run.StepsJson = JsonSerializer.Serialize(steps, JsonOpts);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto publish run {RunId} live steps flush failed", run.Id);
        }
    }

    private static bool IsMirrorSufficient(ListingDetailDto detail)
    {
        if (!detail.HasRawPayload || detail.FourSellerListingId <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(detail.ProductName))
        {
            return false;
        }

        if (detail.Variations.Count == 0)
        {
            return false;
        }

        var hasImage = !string.IsNullOrWhiteSpace(detail.MainImage)
                       || detail.Images.Any(i => !string.IsNullOrWhiteSpace(i.Url));
        return hasImage;
    }

    private async Task MarkListingSkippedAsync(Guid listingId, string reason, CancellationToken ct)
    {
        var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, ct);
        if (listing is null)
        {
            return;
        }

        listing.AutoPublishSkipped = true;
        listing.AutoPublishSkippedAt = DateTime.UtcNow;
        listing.AutoPublishSkipReason = reason.Length > 500 ? reason[..500] : reason;
        listing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task BlockShopForDayLimitAsync(Guid shopId, CancellationToken ct)
    {
        var shopEntity = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, ct);
        if (shopEntity is null)
        {
            return;
        }

        shopEntity.AutoPublishBlockedUntil = ShopListingSlotRules.NextVietnamMidnightUtc(DateTime.UtcNow);
        shopEntity.AutoPublishBlockReason = ListingPublishLimitErrors.DayLimitBlockReason;
        shopEntity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Auto publish blocked shop {ShopId} until {BlockedUntil:o} ({Reason})",
            shopId,
            shopEntity.AutoPublishBlockedUntil,
            shopEntity.AutoPublishBlockReason);
    }

    /// <summary>
    /// Optimistic slot fill after 4Seller accepts. Does not SaveChanges — caller persists with the listing.
    /// </summary>
    private async Task ApplyPublishLimitPublishedIncrementAsync(Guid shopId, CancellationToken ct)
    {
        var shopEntity = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, ct);
        if (shopEntity is null)
        {
            return;
        }

        shopEntity.PublishLimitPublished = ShopListingSlotRules.NextPublishedCount(
            shopEntity.PublishLimitPublished,
            shopEntity.PublishLimitTotal);
        shopEntity.UpdatedAt = DateTime.UtcNow;
    }

    private async Task EnsurePreGatesAsync(AutoPublishContext ctx, CancellationToken ct)
    {
        var enabled = await _settings.GetValueAsync(ListingAutoPublishSettingKeys.Enabled, ct);
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Auto publish hệ thống đã tắt.");
        }

        if (ctx.Shop is null)
        {
            throw new BusinessException("Listing chưa gắn shop.");
        }

        if (!ctx.Shop.AutoPublishEnabled)
        {
            throw new BusinessException("Auto publish shop đã tắt.");
        }

        if (!string.Equals(ctx.Shop.Status, ShopStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Shop không ở trạng thái Active.");
        }

        if (string.IsNullOrWhiteSpace(ctx.Shop.FourSellerShopId))
        {
            throw new BusinessException("Shop chưa kết nối 4Seller.");
        }

        if (ctx.Listing.AutoPublishSkipped)
        {
            throw new BusinessException("Listing đã bị skip auto publish.");
        }

        if (PublishStatuses.IsInFlight(ctx.Listing.PublishStatus))
        {
            throw new BusinessException("Listing đang publish in-flight.");
        }

        if (!string.Equals(ctx.Listing.PageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Listing không ở trạng thái draft.");
        }

        if (string.IsNullOrWhiteSpace(ctx.SizeChartImageUrl))
        {
            throw new BusinessException("Thiếu SizeChartImageUrl trên supplier.");
        }

        if (ShopListingSlotRules.IsBlocked(ctx.Shop.AutoPublishBlockedUntil, DateTime.UtcNow))
        {
            throw new BusinessException("Shop đang bị chặn auto publish (Day_Limit).");
        }

        if (ShopListingSlotRules.IsFull(ctx.Shop.PublishLimitPublished ?? 0, ctx.Shop.PublishLimitTotal))
        {
            throw new BusinessException("Shop đã hết slot listing.");
        }

        if (_sellerLease.HasActiveExtensionLease(ctx.Listing.OwnerId))
        {
            throw new BusinessException("Extension đang sync — chờ extension.");
        }
    }

    private async Task EnsurePublishGatesAsync(Guid listingId, CancellationToken ct)
    {
        var listing = await _db.Listings.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == listingId, ct)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        if (PublishStatuses.IsInFlight(listing.PublishStatus))
        {
            throw new BusinessException("Listing đang publish in-flight.");
        }

        if (!string.Equals(listing.PageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Listing không còn ở trạng thái draft.");
        }
    }

    private static bool ShouldSkipListingPermanently(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        if (ex is not BusinessException biz)
        {
            return false;
        }

        var msg = biz.Message;
        if (msg.Contains("Extension đang sync", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Seller đang sync", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Auto publish hệ thống đã tắt", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Auto publish shop đã tắt", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Auto publish đã bị tắt", StringComparison.OrdinalIgnoreCase)
            || ListingPublishLimitErrors.IsShopCapacityFailure(msg))
        {
            return false;
        }

        return true;
    }

    private async Task<AutoPublishContext> LoadContextAsync(Guid listingId, CancellationToken ct)
    {
        var listing = await _db.Listings.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == listingId, ct)
            ?? throw new NotFoundException("Không tìm thấy listing.");

        var shop = listing.ShopId.HasValue
            ? await _db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == listing.ShopId.Value, ct)
            : null;

        string? sizeChartUrl = null;
        if (listing.SupplierProductInfoId.HasValue)
        {
            sizeChartUrl = await _db.SupplierProductInfos.AsNoTracking()
                .Where(spi => spi.Id == listing.SupplierProductInfoId.Value)
                .Select(spi => spi.SizeChartImageUrl)
                .FirstOrDefaultAsync(ct);
        }

        return new AutoPublishContext(listing, shop, sizeChartUrl);
    }

    private sealed record AutoPublishContext(
        Domain.Entities.Listing Listing,
        Domain.Entities.Shop? Shop,
        string? SizeChartImageUrl);
}
