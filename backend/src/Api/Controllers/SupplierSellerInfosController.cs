using Api.Extensions;
using Application.DTOs.Supplier;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>Seller-scoped supplier shop catalog — JWT owner only.</summary>
[ApiController]
[Route("api/v1/supplier-sellers")]
[Authorize]
public class SupplierSellerInfosController : ControllerBase
{
    private readonly ISupplierSellerInfoService _service;

    /// <summary>Initializes the controller.</summary>
    public SupplierSellerInfosController(ISupplierSellerInfoService service)
    {
        _service = service;
    }

    /// <summary>Paged supplier shops linked to the current seller's listings.</summary>
    [Authorize(Policy = "Permission:seller.supplier-sellers.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<SupplierSellerListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupplierSellerListPageDto>>> List(
        [FromQuery] SupplierSellerListQuery query,
        CancellationToken cancellationToken)
    {
        ScopeToCurrentUser(query);
        var result = await _service.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<SupplierSellerListPageDto>.Ok(result));
    }

    /// <summary>Supplier shop detail scoped to the current seller.</summary>
    [Authorize(Policy = "Permission:seller.supplier-sellers.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SupplierSellerDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupplierSellerDetailDto>>> GetById(
        Guid id,
        [FromQuery] SupplierSellerListQuery query,
        CancellationToken cancellationToken)
    {
        ScopeToCurrentUser(query);
        var result = await _service.GetByIdAsync(id, query, cancellationToken);
        return Ok(ApiResponse<SupplierSellerDetailDto>.Ok(result));
    }

    private void ScopeToCurrentUser(SupplierSellerListQuery query) =>
        query.OwnerUserId = User.GetUserId();
}
