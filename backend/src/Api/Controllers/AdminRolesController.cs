using Application.DTOs.Role;
using Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace Api.Controllers;

/// <summary>
/// Admin role management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin/roles")]
public class AdminRolesController : ControllerBase
{
    private readonly IRoleService _roleService;
    private readonly IValidator<CreateRoleDto> _createValidator;
    private readonly IValidator<UpdateRoleDto> _updateValidator;

    /// <summary>
    /// Initializes the admin roles controller.
    /// </summary>
    public AdminRolesController(
        IRoleService roleService,
        IValidator<CreateRoleDto> createValidator,
        IValidator<UpdateRoleDto> updateValidator)
    {
        _roleService = roleService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// Lists all roles.
    /// </summary>
    [Authorize(Policy = "Permission:admin.roles.view")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<RoleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<RoleDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _roleService.GetAllAsync(cancellationToken);
        return Ok(ApiResponse<List<RoleDto>>.Ok(result));
    }

    /// <summary>
    /// Gets a role by ID.
    /// </summary>
    [Authorize(Policy = "Permission:admin.roles.view")]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<RoleDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RoleDetailDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _roleService.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<RoleDetailDto>.Ok(result));
    }

    /// <summary>
    /// Creates a custom role.
    /// </summary>
    [Authorize(Policy = "Permission:admin.roles.manage")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Create(
        [FromBody] CreateRoleDto request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _roleService.CreateAsync(request, cancellationToken);
        return Ok(ApiResponse<RoleDto>.Ok(result, "Tạo vai trò thành công."));
    }

    /// <summary>
    /// Updates a role.
    /// </summary>
    [Authorize(Policy = "Permission:admin.roles.manage")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Update(
        Guid id,
        [FromBody] UpdateRoleDto request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await _roleService.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<RoleDto>.Ok(result, "Cập nhật vai trò thành công."));
    }

    /// <summary>
    /// Deletes a custom role.
    /// </summary>
    [Authorize(Policy = "Permission:admin.roles.manage")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _roleService.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(null, "Xóa vai trò thành công."));
    }

    /// <summary>
    /// Sets permissions for a role.
    /// </summary>
    [Authorize(Policy = "Permission:admin.roles.manage")]
    [HttpPut("{id:guid}/permissions")]
    [ProducesResponseType(typeof(ApiResponse<RoleDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RoleDetailDto>>> SetPermissions(
        Guid id,
        [FromBody] SetRolePermissionsDto request,
        CancellationToken cancellationToken)
    {
        var result = await _roleService.SetPermissionsAsync(id, request, cancellationToken);
        return Ok(ApiResponse<RoleDetailDto>.Ok(result, "Cập nhật phân quyền thành công."));
    }
}
