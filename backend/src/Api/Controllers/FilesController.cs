using Application.DTOs.File;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// File management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/files")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly IValidator<CheckDuplicateDto> _checkDuplicateValidator;

    /// <summary>
    /// Initializes the files controller.
    /// </summary>
    public FilesController(
        IFileService fileService,
        IValidator<CheckDuplicateDto> checkDuplicateValidator)
    {
        _fileService = fileService;
        _checkDuplicateValidator = checkDuplicateValidator;
    }

    /// <summary>
    /// Lists files for the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.files.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<FileDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<FileDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _fileService.GetAllAsync(User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<List<FileDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a file by ID.
    /// </summary>
    [Authorize(Policy = "Permission:seller.files.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<FileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FileDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _fileService.GetByIdAsync(User.GetOwnerId(), id, cancellationToken);
        return Ok(ApiResponse<FileDto>.Ok(result));
    }

    /// <summary>
    /// Uploads a file with duplicate detection.
    /// </summary>
    [Authorize(Policy = "Permission:seller.files.manage")]
    [HttpPost("upload")]
    [ProducesResponseType(typeof(ApiResponse<FileDto>), StatusCodes.Status200OK)]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<FileDto>>> Upload(
        IFormFile file,
        [FromForm] string? linkedEntityType,
        [FromForm] Guid? linkedEntityId,
        [FromForm] string? metadata,
        CancellationToken cancellationToken)
    {
        var result = await _fileService.UploadAsync(
            User.GetOwnerId(),
            file,
            linkedEntityType,
            linkedEntityId,
            metadata,
            cancellationToken);

        var message = result.IsDuplicate ? "File đã tồn tại (trùng hash)." : "Upload file thành công.";
        return Ok(ApiResponse<FileDto>.Ok(result, message));
    }

    /// <summary>
    /// Checks if a file hash already exists.
    /// </summary>
    [Authorize(Policy = "Permission:seller.files.view")]
    [HttpPost("check-duplicate")]
    [ProducesResponseType(typeof(ApiResponse<FileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<FileDto?>>> CheckDuplicate(
        [FromBody] CheckDuplicateDto request,
        CancellationToken cancellationToken)
    {
        await _checkDuplicateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _fileService.CheckDuplicateAsync(User.GetOwnerId(), request.FileHash, cancellationToken);
        return Ok(ApiResponse<FileDto?>.Ok(result));
    }

    /// <summary>
    /// Deletes a file.
    /// </summary>
    [Authorize(Policy = "Permission:seller.files.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _fileService.DeleteAsync(User.GetOwnerId(), id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa file thành công."));
    }
}
