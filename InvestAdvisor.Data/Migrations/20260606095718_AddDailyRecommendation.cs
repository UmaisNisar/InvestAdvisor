using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyRecommendation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyRecommendation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Caution = table.Column<string>(type: "TEXT", nullable: false),
                    StocksJson = table.Column<string>(type: "TEXT", nullable: false),
                    EtfsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CryptoJson = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRecommendation", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRecommendation_GeneratedAtUtc",
                table: "DailyRecommendation",
                column: "GeneratedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyRecommendation");
        }
    }
}
