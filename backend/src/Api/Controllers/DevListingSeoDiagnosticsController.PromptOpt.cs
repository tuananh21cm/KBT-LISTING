using System.Text;
using System.Text.Json;
using Application.DTOs.Listing;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Seed hunt + auto prompt optimize endpoints (Development only).</summary>
public partial class DevListingSeoDiagnosticsController
{
    private static readonly JsonSerializerOptions ArtifactJson = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Runs the same 3 fixtures under seeds 0..N-1 and returns the highest-scoring seed.
    /// Does not write seed into production appsettings.
    /// </summary>
    [HttpPost("rewrite-seed-hunt")]
    public async Task<IActionResult> RewriteSeedHunt(
        [FromQuery] int seeds = 30,
        [FromQuery] int fixtureCount = 3,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var seedCount = Math.Clamp(seeds, 1, 50);
        var fixtures = await LoadAuditDraftsAsync(Math.Clamp(fixtureCount, 1, 10), cancellationToken);
        if (fixtures.Count == 0)
        {
            return Ok(new { note = "no draft fixtures available" });
        }

        var runs = new List<object>();
        int? bestSeed = null;
        double bestScore = double.MinValue;

        for (var seed = 0; seed < seedCount; seed++)
        {
            var items = new List<object>();
            var failures = new List<object>();
            foreach (var draft in fixtures)
            {
                try
                {
                    var result = await _rewrite.RewriteAsync(
                        new RewriteListingDescriptionRequest
                        {
                            Title = draft.ProductName ?? string.Empty,
                            Description = draft.DescriptionHtml ?? string.Empty,
                            CategoryName = draft.CategoryName,
                            Seed = seed
                        },
                        cancellationToken);
                    items.Add(ScoreOne(
                        draft.Id,
                        draft.ProductName ?? string.Empty,
                        ToPlainText(draft.DescriptionHtml),
                        result,
                        verbose: false));
                }
                catch (Exception ex)
                {
                    failures.Add(new { listingId = draft.Id, error = ex.Message });
                }
            }

            var aggregate = Aggregate(items);
            var panelScore = ExtractPanelScore(aggregate, items.Count);
            runs.Add(new
            {
                seed,
                panelScore,
                scored = items.Count,
                failed = failures.Count,
                failures,
                aggregate
            });

            if (panelScore > bestScore)
            {
                bestScore = panelScore;
                bestSeed = seed;
            }
        }

        var payload = new
        {
            fixtureCount = fixtures.Count,
            fixtureIds = fixtures.Select(f => f.Id).ToList(),
            seedsTried = seedCount,
            bestSeed,
            bestScore,
            runs
        };

        await WriteArtifactAsync($"seed-hunt-{DateTime.UtcNow:yyyyMMddTHHmmss}Z.json", payload);
        return Ok(payload);
    }

    /// <summary>
    /// Locks <paramref name="seed"/>, audits <paramref name="take"/> drafts, patches the
    /// single RewriteDescriptionSystemPrompt from top faults, re-audits, keeps or rolls back.
    /// </summary>
    [HttpPost("rewrite-prompt-optimize")]
    public async Task<IActionResult> RewritePromptOptimize(
        [FromQuery] int seed,
        [FromQuery] int take = 30,
        [FromQuery] int maxRounds = 5,
        [FromQuery] bool commit = false,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var rounds = Math.Clamp(maxRounds, 1, 10);
        var count = Math.Clamp(take, 1, 50);
        var history = new List<object>();
        var workingPrompt = _openAiSettings.RewriteDescriptionSystemPrompt ?? string.Empty;
        var bestPrompt = workingPrompt;
        double bestAvg = double.MinValue;
        string? suggestedPath = null;

        for (var round = 1; round <= rounds; round++)
        {
            _openAiSettings.RewriteDescriptionSystemPrompt = workingPrompt;

            var drafts = await LoadAuditDraftsAsync(count, cancellationToken);
            var items = new List<object>();
            var failures = new List<object>();
            foreach (var draft in drafts)
            {
                try
                {
                    var result = await _rewrite.RewriteAsync(
                        new RewriteListingDescriptionRequest
                        {
                            Title = draft.ProductName ?? string.Empty,
                            Description = draft.DescriptionHtml ?? string.Empty,
                            CategoryName = draft.CategoryName,
                            Seed = seed
                        },
                        cancellationToken);
                    items.Add(ScoreOne(
                        draft.Id,
                        draft.ProductName ?? string.Empty,
                        ToPlainText(draft.DescriptionHtml),
                        result,
                        verbose: false));
                }
                catch (Exception ex)
                {
                    failures.Add(new { listingId = draft.Id, error = ex.Message });
                }
            }

            var aggregate = Aggregate(items);
            var avgScore = ExtractAvgScore(aggregate);
            var cleanRatio = items.Count == 0
                ? 0
                : items.Count(r => ((List<string>)((Dictionary<string, object?>)r)["faults"]!).Count == 0)
                  / (double)items.Count;

            var roundPath = await WriteArtifactAsync(
                $"prompt-round-{round}-{DateTime.UtcNow:yyyyMMddTHHmmss}Z.txt",
                workingPrompt);

            history.Add(new
            {
                round,
                avgScore,
                cleanRatio,
                scored = items.Count,
                failed = failures.Count,
                aggregate,
                promptArtifact = roundPath
            });

            if (avgScore >= bestAvg)
            {
                bestAvg = avgScore;
                bestPrompt = workingPrompt;
                suggestedPath = roundPath;
            }
            else
            {
                // Rollback to best so far before next patch attempt.
                workingPrompt = bestPrompt;
                _openAiSettings.RewriteDescriptionSystemPrompt = workingPrompt;
            }

            if (cleanRatio >= 0.8 || avgScore >= 90)
            {
                break;
            }

            var topFaults = ExtractTopFaults(aggregate, 3);
            if (topFaults.Count == 0)
            {
                break;
            }

            var patched = ApplyPromptPatches(workingPrompt, topFaults);
            if (string.Equals(patched, workingPrompt, StringComparison.Ordinal))
            {
                break;
            }

            workingPrompt = patched;
        }

        _openAiSettings.RewriteDescriptionSystemPrompt = bestPrompt;

        if (commit)
        {
            await CommitPromptToAppsettingsAsync(bestPrompt);
        }

        return Ok(new
        {
            seed,
            take = count,
            maxRounds = rounds,
            commit,
            bestAvgScore = bestAvg,
            suggestedPromptPath = suggestedPath,
            rounds = history
        });
    }

    private static double ExtractPanelScore(object aggregate, int scoredCount)
    {
        if (scoredCount == 0) return 0;
        var avg = ExtractAvgScore(aggregate);
        // Prefer avgScore; tie-break already baked into ComputeScore bonuses.
        return avg;
    }

    private static double ExtractAvgScore(object aggregate)
    {
        var prop = aggregate.GetType().GetProperty("avgScore");
        if (prop?.GetValue(aggregate) is double d) return d;
        if (prop?.GetValue(aggregate) is int i) return i;
        return 0;
    }

    private static List<string> ExtractTopFaults(object aggregate, int take)
    {
        var prop = aggregate.GetType().GetProperty("faultCounts");
        if (prop?.GetValue(aggregate) is not Dictionary<string, int> counts || counts.Count == 0)
        {
            return [];
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .Take(take)
            .ToList();
    }

    private static string ApplyPromptPatches(string prompt, IReadOnlyList<string> topFaults)
    {
        var sb = new StringBuilder(prompt);
        var appended = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fault in topFaults)
        {
            var note = fault switch
            {
                "description-forbidden-care-tips" =>
                    "CRITICAL REMINDER: Never emit a ## Care Tips section or any washing/care bullets.",
                "description-forbidden-colors-line" =>
                    "CRITICAL REMINDER: Never emit a Colors / Available colors / color-variant list line.",
                "description-forbidden-sizes-line" =>
                    "CRITICAL REMINDER: Never emit a Sizes / Available sizes / size-option list line — sizes live on the PDP selector.",
                "description-missing-hashtag" or "hashtag-count-low" or "hashtag-count-high"
                    or "hashtag-banned-stem" =>
                    "CRITICAL REMINDER: End with exactly one hashtag line of 15-25 lowercase #tokens; no marketplace/promo/year stems.",
                "description-primary-density-low" or "description-primary-density-high" =>
                    "CRITICAL REMINDER: Use the primary keyword 4-6 times in complete sentences across opening, Why You Will Love It, Product Details, Great For and optionally closing.",
                "search-terms-empty" or "search-terms-count-low" or "search-terms-count-high" =>
                    "CRITICAL REMINDER: After ===SEARCH_TERMS=== output exactly 4-6 lowercase search terms (2-3 primary + 2-3 secondary).",
                "search-terms-desync" =>
                    "CRITICAL REMINDER: Every search term must share tokens with the title or the description you just wrote.",
                "search-terms-duplicate-intent" =>
                    "CRITICAL REMINDER: Do not repeat the same search intent twice (tank top and tanktop count as one).",
                "highlights-empty" or "highlights-count-low" or "highlights-count-high" =>
                    "CRITICAL REMINDER: After ===PRODUCT_HIGHLIGHTS=== output exactly 3-5 benefit-first highlights.",
                "highlights-ungrounded" or "highlights-superlative" or "highlights-too-short"
                    or "highlights-too-long" =>
                    "CRITICAL REMINDER: Highlights must be 40-80 characters, benefit-first, grounded in verified facts, no best/perfect/guaranteed.",
                "title-dropped-source-attribute" =>
                    "CRITICAL REMINDER: Keep every distinctive searchable source token in the search title.",
                "title-invented-garment" or "title-spec-sheet-junk" =>
                    "CRITICAL REMINDER: Do not invent a garment family or pack spec-sheet rows into the title.",
                "description-marketplace-name" =>
                    "CRITICAL REMINDER: Never name other marketplaces in the description.",
                "description-missing-section" =>
                    "CRITICAL REMINDER: Use exactly Why You Will Love It, Product Details, Great For — never Shipping or Care Tips.",
                _ => null
            };

            if (note is null || !appended.Add(note))
            {
                continue;
            }

            sb.Append("\n\n## Harness reinforcement\n").Append(note);
        }

        return sb.ToString();
    }

    private async Task<string> WriteArtifactAsync(string fileName, object payload)
    {
        var dir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "artifacts", "rewrite-prompt-opt"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        if (payload is string text)
        {
            await System.IO.File.WriteAllTextAsync(path, text, Encoding.UTF8);
        }
        else
        {
            await System.IO.File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(payload, ArtifactJson),
                Encoding.UTF8);
        }

        return path;
    }

    private async Task CommitPromptToAppsettingsAsync(string prompt)
    {
        var apiDir = _env.ContentRootPath;
        foreach (var name in new[] { "appsettings.json", "appsettings.Development.example.json" })
        {
            var path = Path.Combine(apiDir, name);
            if (!System.IO.File.Exists(path)) continue;

            await using var stream = System.IO.File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement.Clone();
            // Rewrite via mutable dictionary round-trip
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                await System.IO.File.ReadAllTextAsync(path));
            if (dict is null || !dict.TryGetValue("OpenAI", out var openAiEl)) continue;

            var openAi = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(openAiEl.GetRawText())
                         ?? new Dictionary<string, JsonElement>();
            openAi["RewriteDescriptionSystemPrompt"] =
                JsonSerializer.SerializeToElement(prompt);

            dict["OpenAI"] = JsonSerializer.SerializeToElement(openAi);
            await System.IO.File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(dict, ArtifactJson) + "\n",
                Encoding.UTF8);
        }

        _openAiSettings.RewriteDescriptionSystemPrompt = prompt;
    }
}
