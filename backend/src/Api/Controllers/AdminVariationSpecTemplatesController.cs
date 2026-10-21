using Application.DTOs.VariationSpecTemplate;
using Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin variation spec template management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/variation-spec-templates")]
public class AdminVariationSpecTemplatesController : ControllerBase
{
    private readonly IVariationSpecTemplateService _service;
    private readonly IValidator<CreateVariationSpecTemplateDto> _createValidator;
    private readonly IValidator<UpdateVariationSpecTemplateDto> _updateValidator;
    private readonly IValidator<ReorderVariationSpecTemplatesDto> _reorderValidator;

    /// <summary>
    /// Initializes the controller.
    /// </summary>
    public AdminVariationSpecTemplatesController(
        IVariationSpecTemplateService service,
        IValidator<CreateVariationSpecTemplateDto> createValidator,
        IValidator<UpdateVariationSpecTemplateDto> updateValidator,
        IValidator<ReorderVariationSpecTemplatesDto> reorderValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _reorderValidator = reorderValidator;
    }

    /// <summary>
    /// Lists all templates (optional SpecType filter).
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<VariationSpecTemplateDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<VariationSpecTemplateDto>>>> GetAll(
        [FromQuery] string? specType,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAllAsync(specType, cancellationToken);
        return Ok(ApiResponse<List<VariationSpecTemplateDto>>.Ok(result));
    }

    /// <summary>
    /// Lists active templates for product form pickers.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<VariationSpecTemplateDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<VariationSpecTemplateDto>>>> GetOptions(
        [FromQuery] string? specType,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetOptionsAsync(specType, cancellationToken);
        return Ok(ApiResponse<List<VariationSpecTemplateDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a template by ID.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<VariationSpecTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<VariationSpecTemplateDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<VariationSpecTemplateDto>.Ok(result));
    }

    /// <summary>
    /// Creates a template.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<VariationSpecTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<VariationSpecTemplateDto>>> Create(
        [FromBody] CreateVariationSpecTemplateDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.CreateAsync(request, cancellationToken);
        return Ok(ApiResponse<VariationSpecTemplateDto>.Ok(result, "Tạo template thành công."));
    }

    /// <summary>
    /// Updates a template.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<VariationSpecTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<VariationSpecTemplateDto>>> Update(
        Guid id,
        [FromBody] UpdateVariationSpecTemplateDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<VariationSpecTemplateDto>.Ok(result, "Cập nhật template thành công."));
    }

    /// <summary>
    /// Reorders templates within a SpecType.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpPut("reorder")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Reorder(
        [FromBody] ReorderVariationSpecTemplatesDto request,
        CancellationToken cancellationToken)
    {
        await _reorderValidator.ValidateAndThrowAsync(request, cancellationToken);
        await _service.ReorderAsync(request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Sắp xếp template thành công."));
    }

    /// <summary>
    /// Deletes a template.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa template thành công."));
    }
}
