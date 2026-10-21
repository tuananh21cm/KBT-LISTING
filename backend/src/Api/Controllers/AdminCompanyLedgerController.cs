using Application.DTOs.CompanyLedger;
using Application.Interfaces;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin company income/expense ledger. Shop and seller assignments are optional and independent.
/// </summary>
[ApiController]
[Route("api/v1/admin/company-ledger")]
public class AdminCompanyLedgerController : ControllerBase
{
    private readonly ICompanyLedgerService _service;
    private readonly IValidator<SaveCompanyLedgerEntryDto> _saveValidator;

    /// <summary>Initializes the controller.</summary>
    public AdminCompanyLedgerController(
        ICompanyLedgerService service,
        IValidator<SaveCompanyLedgerEntryDto> saveValidator)
    {
        _service = service;
        _saveValidator = saveValidator;
    }

    /// <summary>Fixed categories for the form.</summary>
    [Authorize(Policy = "Permission:admin.company-ledger.view")]
    [HttpGet("categories")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CompanyLedgerCategoryDto>>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<IReadOnlyList<CompanyLedgerCategoryDto>>> GetCategories()
    {
        return Ok(ApiResponse<IReadOnlyList<CompanyLedgerCategoryDto>>.Ok(_service.GetCategories()));
    }

    /// <summary>Filtered rows and per-currency totals. Includes unassigned company rows.</summary>
    [Authorize(Policy = "Permission:admin.company-ledger.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CompanyLedgerListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CompanyLedgerListDto>>> List(
        [FromQuery] CompanyLedgerQuery query,
        CancellationToken cancellationToken)
    {
        query.SellerScopeUserId = null;
        var result = await _service.ListAsync(query, cancellationToken);
        return Ok(ApiResponse<CompanyLedgerListDto>.Ok(result));
    }

    /// <summary>Creates a row. Shop acquisition with a shop updates the existing unique row.</summary>
    [Authorize(Policy = "Permission:admin.company-ledger.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CompanyLedgerEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CompanyLedgerEntryDto>>> Create(
        [FromBody] SaveCompanyLedgerEntryDto request,
        CancellationToken cancellationToken)
    {
        await _saveValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.CreateAsync(request, User.GetUserId(), cancellationToken);
        return Ok(ApiResponse<CompanyLedgerEntryDto>.Ok(result, "Đã lưu khoản thu/chi."));
    }

    /// <summary>Updates a row.</summary>
    [Authorize(Policy = "Permission:admin.company-ledger.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CompanyLedgerEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CompanyLedgerEntryDto>>> Update(
        Guid id,
        [FromBody] SaveCompanyLedgerEntryDto request,
        CancellationToken cancellationToken)
    {
        await _saveValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<CompanyLedgerEntryDto>.Ok(result, "Đã cập nhật khoản thu/chi."));
    }

    /// <summary>Deletes a row.</summary>
    [Authorize(Policy = "Permission:admin.company-ledger.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null!, "Đã xóa khoản thu/chi."));
    }
}
