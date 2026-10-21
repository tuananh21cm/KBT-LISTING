using Application.DTOs.Platform;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin platform management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/platforms")]
public class AdminPlatformsController : ControllerBase
{
    private readonly IPlatformService _platformService;
    private readonly IValidator<CreatePlatformDto> _createValidator;
    private readonly IValidator<UpdatePlatformDto> _updateValidator;

    /// <summary>
    /// Initializes the admin platforms controller.
    /// </summary>
    public AdminPlatformsController(
        IPlatformService platformService,
        IValidator<CreatePlatformDto> createValidator,
        IValidator<UpdatePlatformDto> updateValidator)
    {
        _platformService = platformService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// Lists all platforms.
    /// </summary>
    [Authorize(Policy = "Permission:admin.platforms.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<PlatformDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<PlatformDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _platformService.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<List<PlatformDto>>.Ok(result));
    }

    /// <summary>
    /// Lists active platforms for dropdowns.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<PlatformOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<PlatformOptionDto>>>> GetOptions(CancellationToken cancellationToken)
    {
        var result = await _platformService.GetOptionsAsync(cancellationToken);
        return Ok(ApiResponse<List<PlatformOptionDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a platform by ID.
    /// </summary>
    [Authorize(Policy = "Permission:admin.platforms.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PlatformDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PlatformDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _platformService.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<PlatformDto>.Ok(result));
    }

    /// <summary>
    /// Creates a platform.
    /// </summary>
    [Authorize(Policy = "Permission:admin.platforms.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PlatformDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PlatformDto>>> Create(
        [FromBody] CreatePlatformDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _platformService.CreateAsync(request, cancellationToken);
        return Ok(ApiResponse<PlatformDto>.Ok(result, "Tạo nền tảng thành công."));
    }

    /// <summary>
    /// Updates a platform.
    /// </summary>
    [Authorize(Policy = "Permission:admin.platforms.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PlatformDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PlatformDto>>> Update(
        Guid id,
        [FromBody] UpdatePlatformDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _platformService.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<PlatformDto>.Ok(result, "Cập nhật nền tảng thành công."));
    }

    /// <summary>
    /// Deletes a platform.
    /// </summary>
    [Authorize(Policy = "Permission:admin.platforms.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _platformService.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa nền tảng thành công."));
    }
}
