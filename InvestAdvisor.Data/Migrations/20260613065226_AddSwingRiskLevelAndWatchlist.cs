using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSwingRiskLevelAndWatchlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SwingRiskLevel",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SwingWatchItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Close = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CompositeScore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Rsi = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    RegimeDistancePct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TrendDistancePct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SwingWatchItem", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "RuntimeSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "SwingRiskLevel",
                value: 1);

            migrationBuilder.CreateIndex(
                name: "IX_SwingWatchItem_GeneratedAtUtc",
                table: "SwingWatchItem",
                column: "GeneratedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SwingWatchItem");

            migrationBuilder.DropColumn(
                name: "SwingRiskLevel",
                table: "RuntimeSettings");
        }
    }
}
