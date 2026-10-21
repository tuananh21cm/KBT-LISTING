using Application.DTOs.PricingTemplate;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Per-user pricing template endpoints for batch sale-price calculation.
/// </summary>
[ApiController]
[Route("api/v1/pricing-templates")]
public class PricingTemplatesController : ControllerBase
{
    private readonly IPricingTemplateService _service;
    private readonly IValidator<CreatePricingTemplateDto> _createValidator;
    private readonly IValidator<UpdatePricingTemplateDto> _updateValidator;

    /// <summary>
    /// Initializes the controller.
    /// </summary>
    public PricingTemplatesController(
        IPricingTemplateService service,
        IValidator<CreatePricingTemplateDto> createValidator,
        IValidator<UpdatePricingTemplateDto> updateValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// Lists pricing templates for the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<PricingTemplateDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<PricingTemplateDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _service.GetAllAsync(User.GetOwnerId(), cancellationToken);
        return Ok(ApiResponse<List<PricingTemplateDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a pricing template by ID.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PricingTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PricingTemplateDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(User.GetOwnerId(), id, cancellationToken);
        return Ok(ApiResponse<PricingTemplateDto>.Ok(result));
    }

    /// <summary>
    /// Creates a pricing template for the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PricingTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PricingTemplateDto>>> Create(
        [FromBody] CreatePricingTemplateDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.CreateAsync(User.GetOwnerId(), request, cancellationToken);
        return Ok(ApiResponse<PricingTemplateDto>.Ok(result, "Tạo pricing template thành công."));
    }

    /// <summary>
    /// Updates a pricing template owned by the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PricingTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PricingTemplateDto>>> Update(
        Guid id,
        [FromBody] UpdatePricingTemplateDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.UpdateAsync(User.GetOwnerId(), id, request, cancellationToken);
        return Ok(ApiResponse<PricingTemplateDto>.Ok(result, "Cập nhật pricing template thành công."));
    }

    /// <summary>
    /// Deletes a pricing template owned by the current user.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(User.GetOwnerId(), id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa pricing template thành công."));
    }
}
