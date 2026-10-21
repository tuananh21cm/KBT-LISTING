using Application.DTOs.Platform;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller platform lookup endpoints.
/// </summary>
[ApiController]
[Route("api/v1/platforms")]
public class PlatformsController : ControllerBase
{
    private readonly IPlatformService _platformService;

    /// <summary>
    /// Initializes the platforms controller.
    /// </summary>
    public PlatformsController(IPlatformService platformService)
    {
        _platformService = platformService;
    }

    /// <summary>
    /// Lists active platforms for dropdowns (same catalog as admin Platforms).
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.view")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<PlatformOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<PlatformOptionDto>>>> GetOptions(
        CancellationToken cancellationToken)
    {
        var result = await _platformService.GetOptionsAsync(cancellationToken);
        return Ok(ApiResponse<List<PlatformOptionDto>>.Ok(result));
    }
}
