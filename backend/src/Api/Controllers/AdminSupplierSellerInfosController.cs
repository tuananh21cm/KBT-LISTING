using Application.DTOs.Supplier;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>Admin supplier shop (NCC) catalog — read-only list / detail + revenue.</summary>
[ApiController]
[Route("api/v1/admin/supplier-sellers")]
public class AdminSupplierSellerInfosController : ControllerBase
{
    private readonly ISupplierSellerInfoService _service;

    /// <summary>Initializes the controller.</summary>
    public AdminSupplierSellerInfosController(ISupplierSellerInfoService service)
    {
        _service = service;
    }

    /// <summary>Paged supplier shops with period KPI columns.</summary>
    [Authorize(Policy = "Permission:admin.supplier-sellers.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<SupplierSellerListPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupplierSellerListPageDto>>> List(
        [FromQuery] SupplierSellerListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _service.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<SupplierSellerListPageDto>.Ok(result));
    }

    /// <summary>Supplier shop detail + KPI + linked source products / listings.</summary>
    [Authorize(Policy = "Permission:admin.supplier-sellers.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SupplierSellerDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SupplierSellerDetailDto>>> GetById(
        Guid id,
        [FromQuery] SupplierSellerListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, query, cancellationToken);
        return Ok(ApiResponse<SupplierSellerDetailDto>.Ok(result));
    }
}
