using System.Net.Http.Json;
using System.Text.Json;

namespace RewritePromptOpt;

/// <summary>
/// Calls the Development SEO harness on a running API: seed hunt then prompt optimize.
/// Usage: RewritePromptOpt [baseUrl] [seeds] [take] [maxRounds]
/// Example: RewritePromptOpt http://localhost:5000 30 30 5
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        var baseUrl = (args.ElementAtOrDefault(0) ?? "http://localhost:5000").TrimEnd('/');
        var seeds = int.TryParse(args.ElementAtOrDefault(1), out var s) ? s : 30;
        var take = int.TryParse(args.ElementAtOrDefault(2), out var t) ? t : 30;
        var maxRounds = int.TryParse(args.ElementAtOrDefault(3), out var r) ? r : 5;

        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };

        Console.WriteLine($"Seed hunt → {baseUrl}/api/v1/dev/seo/rewrite-seed-hunt?seeds={seeds}&fixtureCount=3");
        var hunt = await http.PostAsync(
            $"{baseUrl}/api/v1/dev/seo/rewrite-seed-hunt?seeds={seeds}&fixtureCount=3",
            null);
        var huntBody = await hunt.Content.ReadAsStringAsync();
        if (!hunt.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"Seed hunt failed: {(int)hunt.StatusCode} {huntBody}");
            return 1;
        }

        using var huntDoc = JsonDocument.Parse(huntBody);
        var bestSeed = huntDoc.RootElement.GetProperty("bestSeed").GetInt32();
        var bestScore = huntDoc.RootElement.GetProperty("bestScore").GetDouble();
        Console.WriteLine($"bestSeed={bestSeed} bestScore={bestScore}");
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                bestSeed,
                bestScore,
                fixtureCount = huntDoc.RootElement.GetProperty("fixtureCount").GetInt32()
            },
            Json));

        Console.WriteLine(
            $"Optimize → …/rewrite-prompt-optimize?seed={bestSeed}&take={take}&maxRounds={maxRounds}");
        var opt = await http.PostAsync(
            $"{baseUrl}/api/v1/dev/seo/rewrite-prompt-optimize?seed={bestSeed}&take={take}&maxRounds={maxRounds}&commit=false",
            null);
        var optBody = await opt.Content.ReadAsStringAsync();
        if (!opt.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"Optimize failed: {(int)opt.StatusCode} {optBody}");
            return 1;
        }

        using var optDoc = JsonDocument.Parse(optBody);
        Console.WriteLine($"bestAvgScore={optDoc.RootElement.GetProperty("bestAvgScore")}");
        if (optDoc.RootElement.TryGetProperty("rounds", out var rounds))
        {
            foreach (var round in rounds.EnumerateArray())
            {
                var n = round.GetProperty("round").GetInt32();
                var avg = round.GetProperty("avgScore").GetDouble();
                var clean = round.GetProperty("cleanRatio").GetDouble();
                var meta = 0;
                if (round.TryGetProperty("aggregate", out var agg)
                    && agg.TryGetProperty("metaComplete", out var mc))
                {
                    meta = mc.GetInt32();
                }

                Console.WriteLine(
                    $"round {n}: avgScore={avg:0.0} cleanRatio={clean:0.00} metaComplete={meta}");
            }
        }

        if (optDoc.RootElement.TryGetProperty("suggestedPromptPath", out var path)
            && path.ValueKind == JsonValueKind.String)
        {
            Console.WriteLine($"suggestedPromptPath={path.GetString()}");
            Console.WriteLine("Re-run with commit=true on the API to write appsettings when ready.");
        }

        return 0;
    }
}
