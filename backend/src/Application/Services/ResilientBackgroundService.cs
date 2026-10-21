using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// Base for every background loop in this app.
///
/// <see cref="BackgroundService.ExecuteAsync"/> is sealed here because .NET defaults
/// <c>HostOptions.BackgroundServiceExceptionBehavior</c> to <c>StopHost</c>: one unhandled
/// exception escaping one loop tears down the whole API, taking order sync, Telegram and every
/// other job with it. A single transient Postgres connect timeout inside a settings read is
/// enough, and that is exactly what happened on 2026-09-30 18:38.
///
/// Subclasses implement <see cref="RunAsync"/> and may throw freely: the supervisor logs the
/// crash, waits <see cref="RestartDelay"/>, then restarts the loop.
/// </summary>
public abstract class ResilientBackgroundService : BackgroundService
{
    /// <summary>Initializes the supervisor with the subclass logger.</summary>
    protected ResilientBackgroundService(ILogger logger)
    {
        Logger = logger;
    }

    /// <summary>Logger of the concrete service, used for the crash / restart lines.</summary>
    protected ILogger Logger { get; }

    /// <summary>How long to wait before restarting a crashed loop.</summary>
    protected virtual TimeSpan RestartDelay => TimeSpan.FromSeconds(30);

    /// <summary>
    /// The actual loop. Runs until <paramref name="stoppingToken"/> fires; returning normally
    /// means the service is done and will not be restarted.
    /// </summary>
    protected abstract Task RunAsync(CancellationToken stoppingToken);

    /// <inheritdoc />
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceName = GetType().Name;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                BackgroundServiceHealthRegistry.RecordCrash(serviceName, ex);
                Logger.LogCritical(
                    ex,
                    "{Service} loop crashed and will restart in {DelaySeconds:0}s",
                    serviceName,
                    RestartDelay.TotalSeconds);
            }

            try
            {
                await Task.Delay(RestartDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
