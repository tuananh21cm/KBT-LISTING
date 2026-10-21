using Application.DTOs.KbtCrm;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// KBT CRM → local sync (teams, users, shops). Replaces the Excel import for shops and seller accounts.
/// </summary>
[ApiController]
[Route("api/v1/admin/kbtcrm")]
[Produces("application/json")]
public class AdminKbtCrmController : ControllerBase
{
    private readonly IKbtCrmOrgSyncService _sync;

    public AdminKbtCrmController(IKbtCrmOrgSyncService sync)
    {
        _sync = sync;
    }

    /// <summary>
    /// Pulls <c>GET /agent/org</c> from CRM now: upserts users by username (team account = owner,
    /// members get TeamOwnerId), upserts shops by TikTok shop code, hands shops to the right team.
    /// Needs <c>kbtcrm.base_url</c> and <c>kbtcrm.agent_secret</c> in Settings.
    /// </summary>
    [Authorize(Policy = "Permission:admin.shops.manage")]
    [HttpPost("sync")]
    [ProducesResponseType(typeof(ApiResponse<KbtCrmSyncResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<KbtCrmSyncResultDto>>> Sync(CancellationToken cancellationToken)
    {
        var result = await _sync.RunAsync("manual", cancellationToken);
        var message =
            $"Đồng bộ CRM xong: user +{result.UsersCreated} / sửa {result.UsersUpdated} / khoá {result.UsersDeactivated}, " +
            $"shop +{result.ShopsCreated} / sửa {result.ShopsUpdated} / chuyển tổ {result.OwnersTransferred}" +
            (result.Skipped.Count > 0 ? $", bỏ qua {result.Skipped.Count}." : ".");
        return Ok(ApiResponse<KbtCrmSyncResultDto>.Ok(result, message));
    }
}
