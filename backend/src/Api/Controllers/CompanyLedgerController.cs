using Application.DTOs.CompanyLedger;
using Application.Interfaces;
using Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller read-only view of company ledger rows assigned to them or to a shop they currently own.
/// </summary>
[ApiController]
[Route("api/v1/company-ledger")]
public class CompanyLedgerController : ControllerBase
{
    private readonly ICompanyLedgerService _service;

    /// <summary>Initializes the controller.</summary>
    public CompanyLedgerController(ICompanyLedgerService service)
    {
        _service = service;
    }

    /// <summary>Fixed categories (labels only).</summary>
    [Authorize(Policy = "Permission:seller.company-ledger.view")]
    [HttpGet("categories")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CompanyLedgerCategoryDto>>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<IReadOnlyList<CompanyLedgerCategoryDto>>> GetCategories()
    {
        return Ok(ApiResponse<IReadOnlyList<CompanyLedgerCategoryDto>>.Ok(_service.GetCategories()));
    }

    /// <summary>
    /// Rows where <c>OwnerUserId</c> is the current user or <c>ShopId</c> is a shop they own.
    /// Rows with neither assignment are hidden.
    /// </summary>
    [Authorize(Policy = "Permission:seller.company-ledger.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CompanyLedgerListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CompanyLedgerListDto>>> List(
        [FromQuery] CompanyLedgerQuery query,
        CancellationToken cancellationToken)
    {
        query.SellerScopeUserId = User.GetUserId();
        query.OwnerUserId = null;
        query.OwnerUnassigned = false;
        var result = await _service.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<CompanyLedgerListDto>.Ok(result));
    }
}
