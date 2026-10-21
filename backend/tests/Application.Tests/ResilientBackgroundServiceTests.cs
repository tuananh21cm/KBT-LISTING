using Application.Services;
using Infrastructure.Integrations.FourSeller;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests;

public class ResilientBackgroundServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task CrashingLoop_RestartsInsteadOfFaultingTheHost()
    {
        var service = new CrashingLoopService(crashesBeforeSuccess: 2);

        await service.StartAsync(CancellationToken.None);
        await service.ReachedSteadyState.Task.WaitAsync(Timeout);

        Assert.Equal(3, service.Attempts);
        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask!.IsFaulted);

        await service.StopAsync(CancellationToken.None);

        Assert.False(service.ExecuteTask!.IsFaulted);
    }

    [Fact]
    public async Task CrashingLoop_IsRecordedForTheWatchdog()
    {
        var service = new RegistryProbeLoopService();

        await service.StartAsync(CancellationToken.None);
        await service.ReachedSteadyState.Task.WaitAsync(Timeout);
        await service.StopAsync(CancellationToken.None);

        var crash = Assert.Single(
            BackgroundServiceHealthRegistry.Snapshot(),
            c => c.ServiceName == nameof(RegistryProbeLoopService));

        Assert.Equal(1, crash.CrashCount);
        Assert.Equal(RegistryProbeLoopService.CrashMessage, crash.Message);
    }

    [Fact]
    public async Task Cancellation_EndsTheLoopWithoutRestarting()
    {
        var service = new IdleLoopService();

        await service.StartAsync(CancellationToken.None);
        await service.Started.Task.WaitAsync(Timeout);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, service.Attempts);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    /// <summary>
    /// The regression guard for 2026-09-30: a loop deriving straight from
    /// <see cref="BackgroundService"/> can throw past the host and take the whole API down.
    /// </summary>
    [Fact]
    public void EveryBackgroundService_DerivesFromResilientBackgroundService()
    {
        var assemblies = new[]
        {
            typeof(ResilientBackgroundService).Assembly,
            typeof(OrderSyncBackgroundService).Assembly
        };

        var offenders = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsClass: true }
                && typeof(BackgroundService).IsAssignableFrom(t)
                && !typeof(ResilientBackgroundService).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }

    private sealed class CrashingLoopService : ResilientBackgroundService
    {
        private readonly int _crashesBeforeSuccess;

        public CrashingLoopService(int crashesBeforeSuccess)
            : base(NullLogger.Instance)
        {
            _crashesBeforeSuccess = crashesBeforeSuccess;
        }

        public TaskCompletionSource ReachedSteadyState { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Attempts { get; private set; }

        protected override TimeSpan RestartDelay => TimeSpan.FromMilliseconds(10);

        protected override Task RunAsync(CancellationToken stoppingToken)
        {
            Attempts++;
            if (Attempts <= _crashesBeforeSuccess)
            {
                throw new InvalidOperationException("transient failure");
            }

            ReachedSteadyState.TrySetResult();
            return Task.Delay(System.Threading.Timeout.Infinite, stoppingToken);
        }
    }

    private sealed class RegistryProbeLoopService : ResilientBackgroundService
    {
        public const string CrashMessage = "registry probe failure";

        public RegistryProbeLoopService()
            : base(NullLogger.Instance)
        {
        }

        public TaskCompletionSource ReachedSteadyState { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _attempts;

        protected override TimeSpan RestartDelay => TimeSpan.FromMilliseconds(10);

        protected override Task RunAsync(CancellationToken stoppingToken)
        {
            if (++_attempts == 1)
            {
                throw new InvalidOperationException(CrashMessage);
            }

            ReachedSteadyState.TrySetResult();
            return Task.Delay(System.Threading.Timeout.Infinite, stoppingToken);
        }
    }

    private sealed class IdleLoopService : ResilientBackgroundService
    {
        public IdleLoopService()
            : base(NullLogger.Instance)
        {
        }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Attempts { get; private set; }

        protected override Task RunAsync(CancellationToken stoppingToken)
        {
            Attempts++;
            Started.TrySetResult();
            return Task.Delay(System.Threading.Timeout.Infinite, stoppingToken);
        }
    }
}
