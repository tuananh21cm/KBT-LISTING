using System.Globalization;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Development-only Telegram diagnostics (force-send summaries). Returns 404 outside Development.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/dev/diagnostics/telegram")]
[ApiExplorerSettings(IgnoreApi = true)]
public class DevTelegramDiagnosticsController : ControllerBase
{
    private readonly ITelegramNotifier _telegram;
    private readonly IWebHostEnvironment _env;

    /// <summary>Initializes the Telegram diagnostics controller.</summary>
    public DevTelegramDiagnosticsController(ITelegramNotifier telegram, IWebHostEnvironment env)
    {
        _telegram = telegram;
        _env = env;
    }

    /// <summary>
    /// Force-sends seller summary for a VN local calendar day.
    /// <paramref name="date"/> = <c>yyyy-MM-dd</c> or <c>yesterday</c> (default yesterday).
    /// </summary>
    [HttpPost("seller-summary")]
    public async Task<IActionResult> ForceSellerSummary(
        [FromQuery] string? date,
        CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        if (!TryResolveLocalDate(date, out var localDate, out var error))
        {
            return BadRequest(new { error });
        }

        var sent = await _telegram.ForceSendSellerSummaryForLocalDateAsync(localDate, cancellationToken);
        return Ok(new
        {
            sent,
            localDate = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            message = sent
                ? $"Đã gửi tổng hợp Seller cho {localDate:dd/MM/yyyy}."
                : "Không gửi được (thiếu BotToken/ChatId hoặc lỗi Telegram — xem log API)."
        });
    }

    /// <summary>
    /// Force-sends one sample Add-track message to <c>shein.order_detail_sync.telegram_chat_id</c>.
    /// </summary>
    [HttpPost("add-track-test")]
    public async Task<IActionResult> ForceAddTrackTest(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var sent = await _telegram.ForceSendSheinDetailAddTrackTestAsync(cancellationToken);
        return Ok(new
        {
            sent,
            message = sent
                ? "Đã gửi tin TEST Add track vào nhóm Chat ID cấu hình."
                : "Không gửi được (thiếu BotToken/Chat ID Add track hoặc lỗi Telegram — xem log API)."
        });
    }

    private static bool TryResolveLocalDate(string? raw, out DateTime localDate, out string error)
    {
        error = "";
        localDate = default;

        var vn = ResolveVietnamTimeZone();
        var vnToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vn).Date;

        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw.Trim(), "yesterday", StringComparison.OrdinalIgnoreCase))
        {
            localDate = vnToday.AddDays(-1);
            return true;
        }

        if (string.Equals(raw.Trim(), "today", StringComparison.OrdinalIgnoreCase))
        {
            localDate = vnToday;
            return true;
        }

        if (DateTime.TryParseExact(
                raw.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            localDate = parsed.Date;
            return true;
        }

        error = "date phải là yyyy-MM-dd, 'yesterday', hoặc 'today'.";
        return false;
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }
}
