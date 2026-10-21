using Application.Interfaces;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

/// <summary>
/// Chạy retention một lượt mỗi ngày.
///
/// Ba bảng nó dọn (snapshot Compass, nhật ký sync TikTok Shop, refresh token đã hết vòng đời) chỉ
/// thêm dòng và trước đây không có luồng nào xoá — mỗi ngày chạy là một lượng cố định cộng thêm.
/// Một ngày một lượt là đủ: cắt được đà tăng mà không thêm tải cho DB, và mỗi lượt là ba câu
/// <c>DELETE … WHERE</c>.
///
/// Cố ý <b>không</b> chạy VACUUM/REINDEX: hai lệnh đó khoá bảng hoặc nhân đôi chỗ đĩa, phải do admin
/// chủ động chạy (xem <c>backend/scripts/db-index-maintenance.sql</c>).
/// </summary>
public class DataRetentionBackgroundService : ResilientBackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(24);

    /// <summary>Sau migrate + seed, và lệch khỏi các job sync để không tranh connection lúc khởi động.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataRetentionBackgroundService> _logger;

    /// <summary>Khởi tạo vòng lặp dọn dữ liệu cũ.</summary>
    public DataRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DataRetentionBackgroundService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!await DelayAsync(StartupDelay, stoppingToken))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var retention = scope.ServiceProvider.GetRequiredService<IDataRetentionService>();
                var deleted = await retention.PurgeAsync(stoppingToken);

                if (deleted.Count > 0)
                {
                    _logger.LogInformation(
                        "Data retention purge: {Summary}",
                        string.Join(", ", deleted.Select(kv => $"{kv.Key}={kv.Value}")));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data retention purge tick failed unexpectedly");
            }

            if (!await DelayAsync(TickInterval, stoppingToken))
            {
                break;
            }
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
