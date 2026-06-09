using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenerScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScreenerScore",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AsOfDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompositeScore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreenerScore", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScreenerScore_AsOfDate",
                table: "ScreenerScore",
                column: "AsOfDate");

            migrationBuilder.CreateIndex(
                name: "IX_ScreenerScore_Ticker_AsOfDate",
                table: "ScreenerScore",
                columns: new[] { "Ticker", "AsOfDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScreenerScore");
        }
    }
}
