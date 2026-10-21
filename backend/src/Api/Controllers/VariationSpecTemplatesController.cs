using Application.DTOs.VariationSpecTemplate;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Seller read-only variation spec template options for product form pickers.
/// </summary>
[ApiController]
[Route("api/v1/variation-spec-templates")]
public class VariationSpecTemplatesController : ControllerBase
{
    private readonly IVariationSpecTemplateService _service;

    /// <summary>
    /// Initializes the controller.
    /// </summary>
    public VariationSpecTemplatesController(IVariationSpecTemplateService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lists active templates for product form pickers.
    /// </summary>
    [Authorize(Policy = "Permission:seller.products.manage")]
    [HttpGet("options")]
    [ProducesResponseType(typeof(ApiResponse<List<VariationSpecTemplateDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<VariationSpecTemplateDto>>>> GetOptions(
        [FromQuery] string? specType,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetOptionsAsync(specType, cancellationToken);
        return Ok(ApiResponse<List<VariationSpecTemplateDto>>.Ok(result));
    }
}
