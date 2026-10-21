using Application.DTOs.ProductSource;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller product source lookup endpoints.
/// </summary>
[ApiController]
[Route("api/v1/product-sources")]
public class ProductSourcesController : ControllerBase
{
    private readonly IProductSourceService _productSourceService;

    /// <summary>
    /// Initializes the product sources controller.
    /// </summary>
    public ProductSourcesController(IProductSourceService productSourceService)
    {
        _productSourceService = productSourceService;
    }

    /// <summary>
    /// Lists active product sources for dropdowns.
    /// </summary>
    [Authorize(Policy = "PermissionAny:seller.products.manage,admin.product-sources.view")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<ProductSourceOptionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ProductSourceOptionDto>>>> GetOptions(CancellationToken cancellationToken)
    {
        var result = await _productSourceService.GetOptionsAsync(cancellationToken);
        return Ok(ApiResponse<List<ProductSourceOptionDto>>.Ok(result));
    }
}
