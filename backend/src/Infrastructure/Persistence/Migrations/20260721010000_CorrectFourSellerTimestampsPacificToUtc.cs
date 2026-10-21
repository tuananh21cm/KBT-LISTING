using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// No-op. This migration originally shifted 4Seller-sourced timestamps by re-interpreting their
/// wall-clock digits as America/Los_Angeles, on the assumption that 4Seller returns Pacific times.
/// That assumption is wrong: 4Seller returns naive strings that are already UTC (verified against
/// raw payloads vs. sync/created instants), so no correction is needed and the original shift would
/// corrupt correct data by +7/+8h. The body is intentionally empty; the parsing fix lives in
/// <c>AppTimeZones.ParseFourSellerNaiveAsUtc</c>.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721010000_CorrectFourSellerTimestampsPacificToUtc")]
public class CorrectFourSellerTimestampsPacificToUtc : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty: 4Seller timestamps are UTC; no data shift required.
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty.
    }
}
