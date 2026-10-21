using Application.DTOs.ProductSource;
using Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin product source management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/product-sources")]
public class AdminProductSourcesController : ControllerBase
{
    private readonly IProductSourceService _productSourceService;
    private readonly IValidator<CreateProductSourceDto> _createValidator;
    private readonly IValidator<UpdateProductSourceDto> _updateValidator;

    /// <summary>
    /// Initializes the admin product sources controller.
    /// </summary>
    public AdminProductSourcesController(
        IProductSourceService productSourceService,
        IValidator<CreateProductSourceDto> createValidator,
        IValidator<UpdateProductSourceDto> updateValidator)
    {
        _productSourceService = productSourceService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// Lists all product sources.
    /// </summary>
    [Authorize(Policy = "Permission:admin.product-sources.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ProductSourceDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ProductSourceDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _productSourceService.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<List<ProductSourceDto>>.Ok(result));
    }

    /// <summary>
    /// Lists active product sources for dropdowns.
    /// </summary>
    [Authorize(Policy = "Permission:admin.products.manage")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<ProductSourceOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ProductSourceOptionDto>>>> GetOptions(CancellationToken cancellationToken)
    {
        var result = await _productSourceService.GetOptionsAsync(cancellationToken);
        return Ok(ApiResponse<List<ProductSourceOptionDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a product source by ID.
    /// </summary>
    [Authorize(Policy = "Permission:admin.product-sources.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProductSourceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductSourceDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _productSourceService.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<ProductSourceDto>.Ok(result));
    }

    /// <summary>
    /// Creates a product source.
    /// </summary>
    [Authorize(Policy = "Permission:admin.product-sources.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ProductSourceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductSourceDto>>> Create(
        [FromBody] CreateProductSourceDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _productSourceService.CreateAsync(request, cancellationToken);
        return Ok(ApiResponse<ProductSourceDto>.Ok(result, "Tạo nguồn hàng thành công."));
    }

    /// <summary>
    /// Updates a product source.
    /// </summary>
    [Authorize(Policy = "Permission:admin.product-sources.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProductSourceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProductSourceDto>>> Update(
        Guid id,
        [FromBody] UpdateProductSourceDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _productSourceService.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<ProductSourceDto>.Ok(result, "Cập nhật nguồn hàng thành công."));
    }

    /// <summary>
    /// Deletes a product source.
    /// </summary>
    [Authorize(Policy = "Permission:admin.product-sources.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _productSourceService.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa nguồn hàng thành công."));
    }
}
