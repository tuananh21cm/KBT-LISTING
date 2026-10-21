using Application.DTOs.Role;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin permission listing endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/permissions")]
public class AdminPermissionsController : ControllerBase
{
    private readonly IRoleService _roleService;

    /// <summary>
    /// Initializes the admin permissions controller.
    /// </summary>
    public AdminPermissionsController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    /// <summary>
    /// Lists all permissions.
    /// </summary>
    [Authorize(Policy = "Permission:admin.roles.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<PermissionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<PermissionDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _roleService.GetAllPermissionsAsync(cancellationToken);
        return Ok(ApiResponse<List<PermissionDto>>.Ok(result));
    }
}
