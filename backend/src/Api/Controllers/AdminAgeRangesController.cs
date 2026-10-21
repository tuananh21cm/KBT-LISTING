using Application.DTOs.Shop;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>Admin read API for shop audience age-bracket presets.</summary>
[ApiController]
[Route("api/v1/admin/age-ranges")]
public class AdminAgeRangesController : ControllerBase
{
    private readonly IAgeRangeService _ageRanges;

    /// <summary>Creates the controller.</summary>
    public AdminAgeRangesController(IAgeRangeService ageRanges)
    {
        _ageRanges = ageRanges;
    }

    /// <summary>Lists active age ranges for catalog Select + info panel.</summary>
    [Authorize(Policy = "Permission:admin.shops.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AgeRangeDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AgeRangeDto>>>> List(
        CancellationToken cancellationToken)
    {
        var result = await _ageRanges.ListActiveAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AgeRangeDto>>.Ok(result));
    }
}
