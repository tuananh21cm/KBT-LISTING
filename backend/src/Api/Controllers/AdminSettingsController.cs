using Application.DTOs.Maintenance;
using Application.DTOs.Setting;
using Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin system settings management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/settings")]
public class AdminSettingsController : ControllerBase
{
    private readonly ISettingService _settingService;
    private readonly IDatabaseStorageStatsService _storageStats;
    private readonly ITelegramNotifier _telegram;
    private readonly IValidator<CreateSettingDto> _createValidator;
    private readonly IValidator<UpdateSettingDto> _updateValidator;

    /// <summary>
    /// Initializes the admin settings controller.
    /// </summary>
    public AdminSettingsController(
        ISettingService settingService,
        IDatabaseStorageStatsService storageStats,
        ITelegramNotifier telegram,
        IValidator<CreateSettingDto> createValidator,
        IValidator<UpdateSettingDto> updateValidator)
    {
        _settingService = settingService;
        _storageStats = storageStats;
        _telegram = telegram;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// Phân bổ dung lượng database: heap / TOAST / index theo bảng, và <c>idx_scan</c> + size từng
    /// index. Dùng để quyết định DROP index dựa trên số đo — index không bao giờ được scan vẫn phải
    /// ghi lại ở mọi UPDATE của bảng.
    /// </summary>
    /// <param name="table">Chỉ lấy index của một bảng (vd. <c>Listings</c>).</param>
    /// <param name="top">Số dòng tối đa mỗi danh sách (1–200, mặc định 30).</param>
    /// <param name="cancellationToken">Token hủy.</param>
    [Authorize(Policy = "Permission:admin.settings.view")]
    [HttpGet("storage-stats")]
    [ProducesResponseType(typeof(ApiResponse<DatabaseStorageStatsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DatabaseStorageStatsDto>>> GetStorageStats(
        [FromQuery] string? table,
        [FromQuery] int top,
        CancellationToken cancellationToken)
    {
        var result = await _storageStats.GetAsync(table, top <= 0 ? 30 : top, cancellationToken);
        return Ok(ApiResponse<DatabaseStorageStatsDto>.Ok(result));
    }

    /// <summary>
    /// Lists all system settings.
    /// </summary>
    [Authorize(Policy = "Permission:admin.settings.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<SettingDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<SettingDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _settingService.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<List<SettingDto>>.Ok(result));
    }

    /// <summary>
    /// Gửi một tin TEST dạng Add track vào Chat ID
    /// <c>shein.order_detail_sync.telegram_chat_id</c> (không cần bật toggle notify, không stamp đơn).
    /// </summary>
    [Authorize(Policy = "Permission:admin.settings.manage")]
    [HttpPost("shein-detail-sync/test-add-track-notify")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> TestSheinDetailAddTrackNotify(
        CancellationToken cancellationToken)
    {
        var sent = await _telegram.ForceSendSheinDetailAddTrackTestAsync(cancellationToken);
        if (!sent)
        {
            return Ok(ApiResponse<object>.Fail(
                "Không gửi được tin TEST (thiếu BotToken / Chat ID Add track hoặc lỗi Telegram — xem log API)."));
        }

        return Ok(ApiResponse<object>.Ok(null, "Đã gửi tin TEST Add track vào nhóm Telegram."));
    }

    /// <summary>
    /// Gets a setting by ID.
    /// </summary>
    [Authorize(Policy = "Permission:admin.settings.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SettingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SettingDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _settingService.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<SettingDto>.Ok(result));
    }

    /// <summary>
    /// Creates a setting.
    /// </summary>
    [Authorize(Policy = "Permission:admin.settings.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SettingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SettingDto>>> Create(
        [FromBody] CreateSettingDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _settingService.CreateAsync(request, cancellationToken);
        return Ok(ApiResponse<SettingDto>.Ok(result, "Tạo cấu hình thành công."));
    }

    /// <summary>
    /// Updates a setting.
    /// </summary>
    [Authorize(Policy = "Permission:admin.settings.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SettingDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SettingDto>>> Update(
        Guid id,
        [FromBody] UpdateSettingDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _settingService.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<SettingDto>.Ok(result, "Cập nhật cấu hình thành công."));
    }

    /// <summary>
    /// Deletes a setting.
    /// </summary>
    [Authorize(Policy = "Permission:admin.settings.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _settingService.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa cấu hình thành công."));
    }
}
