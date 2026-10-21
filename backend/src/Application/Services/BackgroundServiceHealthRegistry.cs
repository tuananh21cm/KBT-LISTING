using System.Collections.Concurrent;

namespace Application.Services;

/// <summary>
/// Last crash recorded for one background loop.
/// </summary>
/// <param name="ServiceName">CLR type name of the <c>BackgroundService</c>.</param>
/// <param name="OccurredUtc">When the loop threw.</param>
/// <param name="Message">Exception message, truncated for Telegram / log lines.</param>
/// <param name="CrashCount">How many times this loop has crashed since process start.</param>
public sealed record BackgroundServiceCrash(
    string ServiceName,
    DateTimeOffset OccurredUtc,
    string Message,
    int CrashCount);

/// <summary>
/// Process-wide record of background loops that crashed and were restarted by
/// <see cref="ResilientBackgroundService"/>.
///
/// A restarted loop leaves no trace other than a log line, so an operator watching only the
/// sync cards cannot tell a healthy quiet period from a loop that has been thrashing. The
/// watchdog reads this to turn those restarts into an alert.
/// </summary>
public static class BackgroundServiceHealthRegistry
{
    private const int MaxMessageLength = 200;

    private static readonly ConcurrentDictionary<string, BackgroundServiceCrash> Crashes = new();

    /// <summary>Records a crash, incrementing the per-service counter.</summary>
    public static void RecordCrash(string serviceName, Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        if (message.Length > MaxMessageLength)
        {
            message = message[..MaxMessageLength];
        }

        Crashes.AddOrUpdate(
            serviceName,
            _ => new BackgroundServiceCrash(serviceName, DateTimeOffset.UtcNow, message, 1),
            (_, previous) => previous with
            {
                OccurredUtc = DateTimeOffset.UtcNow,
                Message = message,
                CrashCount = previous.CrashCount + 1
            });
    }

    /// <summary>All recorded crashes, newest first.</summary>
    public static IReadOnlyList<BackgroundServiceCrash> Snapshot() =>
        Crashes.Values
            .OrderByDescending(c => c.OccurredUtc)
            .ToList();

    /// <summary>Crashes that happened at or after <paramref name="sinceUtc"/>, newest first.</summary>
    public static IReadOnlyList<BackgroundServiceCrash> SnapshotSince(DateTimeOffset sinceUtc) =>
        Crashes.Values
            .Where(c => c.OccurredUtc >= sinceUtc)
            .OrderByDescending(c => c.OccurredUtc)
            .ToList();
}
