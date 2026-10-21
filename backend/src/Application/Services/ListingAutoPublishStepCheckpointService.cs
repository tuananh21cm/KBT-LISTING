using System.Text.Json;
using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>Persists auto publish step checkpoints on the listing row.</summary>
public sealed class ListingAutoPublishStepCheckpointService : IListingAutoPublishStepCheckpointService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IApplicationDbContext _db;

    public ListingAutoPublishStepCheckpointService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<HashSet<string>> EnsureLoadedAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken)
                      ?? throw new Domain.Exceptions.NotFoundException("Không tìm thấy listing.");

        if (!string.Equals(listing.PageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(listing.AutoPublishCompletedStepsJson))
            {
                listing.AutoPublishCompletedStepsJson = null;
                listing.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return [];
        }

        var loaded = ParseStepCodes(listing.AutoPublishCompletedStepsJson);
        return loaded;
    }

    public async Task MarkCompletedAsync(Guid listingId, string stepCode, CancellationToken cancellationToken = default)
    {
        if (!ListingAutoPublishStepCodes.IsKnown(stepCode))
        {
            throw new ArgumentException($"Unknown auto publish step code: {stepCode}", nameof(stepCode));
        }

        var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken)
                      ?? throw new Domain.Exceptions.NotFoundException("Không tìm thấy listing.");

        var codes = ParseStepCodes(listing.AutoPublishCompletedStepsJson);
        if (!codes.Add(stepCode))
        {
            return;
        }

        listing.AutoPublishCompletedStepsJson = SerializeStepCodes(codes);
        listing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken);
        if (listing is null || string.IsNullOrWhiteSpace(listing.AutoPublishCompletedStepsJson))
        {
            return;
        }

        listing.AutoPublishCompletedStepsJson = null;
        listing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task InvalidateFromAsync(Guid listingId, string stepCode, CancellationToken cancellationToken = default)
    {
        if (!ListingAutoPublishStepCodes.IsKnown(stepCode))
        {
            throw new ArgumentException($"Unknown auto publish step code: {stepCode}", nameof(stepCode));
        }

        var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken);
        if (listing is null || string.IsNullOrWhiteSpace(listing.AutoPublishCompletedStepsJson))
        {
            return;
        }

        var codes = ParseStepCodes(listing.AutoPublishCompletedStepsJson);
        var startIndex = Array.FindIndex(
            ListingAutoPublishStepCodes.PipelineOrder,
            code => string.Equals(code, stepCode, StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0)
        {
            return;
        }

        var removed = false;
        foreach (var code in ListingAutoPublishStepCodes.PipelineOrder.Skip(startIndex))
        {
            removed |= codes.Remove(code);
        }

        if (!removed)
        {
            return;
        }

        listing.AutoPublishCompletedStepsJson = codes.Count == 0
            ? null
            : SerializeStepCodes(codes);
        listing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static HashSet<string> ParseStepCodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var codes = JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
            return new HashSet<string>(
                codes.Where(ListingAutoPublishStepCodes.IsKnown),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string SerializeStepCodes(IEnumerable<string> codes)
    {
        var ordered = ListingAutoPublishStepCodes.PipelineOrder
            .Where(code => codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return JsonSerializer.Serialize(ordered, JsonOpts);
    }
}
