using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRealizedLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RealizedLot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    AssetClass = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountType = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    Proceeds = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CostBasis = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false, defaultValue: "USD"),
                    RealizedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, defaultValue: ""),
                    ManualEntry = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RealizedLot", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RealizedLot_TenantId_SourceHash",
                table: "RealizedLot",
                columns: new[] { "TenantId", "SourceHash" },
                unique: true,
                filter: "\"SourceHash\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_RealizedLot_TenantId_Ticker",
                table: "RealizedLot",
                columns: new[] { "TenantId", "Ticker" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RealizedLot");
        }
    }
}
