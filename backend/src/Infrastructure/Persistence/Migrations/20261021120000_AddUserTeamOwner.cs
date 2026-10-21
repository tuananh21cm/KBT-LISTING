using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds <c>AspNetUsers.TeamOwnerId</c>: a member's team account (CRM "tổ"). Seller-scoped
/// requests resolve their owner to this id, so a whole team shares one data scope.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021120000_AddUserTeamOwner")]
public class AddUserTeamOwner : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TeamOwnerId",
            table: "AspNetUsers",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUsers_TeamOwnerId",
            table: "AspNetUsers",
            column: "TeamOwnerId");

        migrationBuilder.AddForeignKey(
            name: "FK_AspNetUsers_AspNetUsers_TeamOwnerId",
            table: "AspNetUsers",
            column: "TeamOwnerId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_AspNetUsers_AspNetUsers_TeamOwnerId", table: "AspNetUsers");
        migrationBuilder.DropIndex(name: "IX_AspNetUsers_TeamOwnerId", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "TeamOwnerId", table: "AspNetUsers");
    }
}
