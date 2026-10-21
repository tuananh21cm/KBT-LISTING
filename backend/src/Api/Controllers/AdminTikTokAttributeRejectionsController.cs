using Application.DTOs.Listing;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Product attribute values TikTok refuses, learned from publish failures.
/// <para>
/// 4Seller keeps offering such a value with a numeric id long after TikTok stops honouring it, so the
/// failure message is the only evidence. These endpoints make the learned list visible, and let an
/// admin unblock a value once TikTok accepts it again.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/admin/tiktok-attribute-rejections")]
public class AdminTikTokAttributeRejectionsController : ControllerBase
{
    private readonly ITikTokAttributeRejectionService _rejections;

    /// <summary>Initializes the admin attribute rejection controller.</summary>
    public AdminTikTokAttributeRejectionsController(ITikTokAttributeRejectionService rejections)
    {
        _rejections = rejections;
    }

    /// <summary>Lists every learned rejection, newest failure first.</summary>
    [Authorize(Policy = "Permission:admin.listings.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<TikTokAttributeValueRejectionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<TikTokAttributeValueRejectionDto>>>> GetAll(
        CancellationToken cancellationToken)
    {
        var result = await _rejections.ListAsync(cancellationToken);
        return Ok(ApiResponse<List<TikTokAttributeValueRejectionDto>>.Ok(result));
    }

    /// <summary>Unblocks a value so Specifics may pick it again.</summary>
    [Authorize(Policy = "Permission:admin.listings.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        var removed = await _rejections.RemoveAsync(id, cancellationToken);
        if (!removed)
        {
            return NotFound(ApiResponse<object>.Fail("Không tìm thấy giá trị bị chặn."));
        }

        return Ok(ApiResponse<object>.Ok(null, "Đã bỏ chặn giá trị này cho lần publish tới."));
    }
}
