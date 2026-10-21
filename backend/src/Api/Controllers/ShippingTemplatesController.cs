using Application.DTOs.ShippingTemplate;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Per-user shipping template endpoints for product form autofill.
/// </summary>
[ApiController]
[Route("api/v1/shipping-templates")]
public class ShippingTemplatesController : ControllerBase
{
    private readonly IShippingTemplateService _service;
    private readonly IValidator<CreateShippingTemplateDto> _createValidator;
    private readonly IValidator<UpdateShippingTemplateDto> _updateValidator;

    /// <summary>
    /// Initializes the controller.
    /// </summary>
    public ShippingTemplatesController(
        IShippingTemplateService service,
        IValidator<CreateShippingTemplateDto> createValidator,
        IValidator<UpdateShippingTemplateDto> updateValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// Lists shipping templates for the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ShippingTemplateDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ShippingTemplateDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _service.GetAllAsync(User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<List<ShippingTemplateDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a shipping template by ID.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ShippingTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShippingTemplateDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(User.GetUserId(), id, cancellationToken);
        return Ok(ApiResponse<ShippingTemplateDto>.Ok(result));
    }

    /// <summary>
    /// Creates a shipping template for the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ShippingTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShippingTemplateDto>>> Create(
        [FromBody] CreateShippingTemplateDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.CreateAsync(User.GetUserId(), request, cancellationToken);
        return Ok(ApiResponse<ShippingTemplateDto>.Ok(result, "Tạo shipping template thành công."));
    }

    /// <summary>
    /// Updates a shipping template owned by the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ShippingTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ShippingTemplateDto>>> Update(
        Guid id,
        [FromBody] UpdateShippingTemplateDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.UpdateAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(ApiResponse<ShippingTemplateDto>.Ok(result, "Cập nhật shipping template thành công."));
    }

    /// <summary>
    /// Deletes a shipping template owned by the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa shipping template thành công."));
    }
}