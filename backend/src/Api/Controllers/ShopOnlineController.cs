using Application.DTOs.Shop;
using Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Anonymous ingest for TikTok Shop extension online-session tracking (ExternalShopId only).
/// </summary>
[ApiController]
[Route("api/v1/shops/tiktok-online")]
[AllowAnonymous]
public class ShopOnlineController : ControllerBase
{
    private readonly IShopOnlineSessionService _sessions;
    private readonly IValidator<ShopOnlineHeartbeatDto> _heartbeatValidator;
    private readonly IValidator<ShopOnlineCloseDto> _closeValidator;

    /// <summary>Initializes the controller.</summary>
    public ShopOnlineController(
        IShopOnlineSessionService sessions,
        IValidator<ShopOnlineHeartbeatDto> heartbeatValidator,
        IValidator<ShopOnlineCloseDto> closeValidator)
    {
        _sessions = sessions;
        _heartbeatValidator = heartbeatValidator;
        _closeValidator = closeValidator;
    }

    /// <summary>Open or refresh an online session while a seller-us tab is open.</summary>
    [HttpPost("heartbeat")]
    [ProducesResponseType(typeof(ApiResponse<ShopOnlineSessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopOnlineSessionDto>>> Heartbeat(
        [FromBody] ShopOnlineHeartbeatDto request,
        CancellationToken cancellationToken)
    {
        await _heartbeatValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _sessions.HeartbeatAsync(request, cancellationToken);
        return Ok(ApiResponse<ShopOnlineSessionDto>.Ok(result));
    }

    /// <summary>Close an online session (tab closed or browser-restart reconcile).</summary>
    [HttpPost("close")]
    [ProducesResponseType(typeof(ApiResponse<ShopOnlineSessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShopOnlineSessionDto>>> Close(
        [FromBody] ShopOnlineCloseDto request,
        CancellationToken cancellationToken)
    {
        await _closeValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _sessions.CloseAsync(request, cancellationToken);
        return Ok(ApiResponse<ShopOnlineSessionDto>.Ok(result));
    }
}
