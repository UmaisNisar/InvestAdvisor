using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSwingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSwingUniverse",
                table: "Stock",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PaperTrade",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EntryLow = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EntryHigh = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EntryReference = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    StopLoss = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Target = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RewardRiskRatio = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    HoldingDays = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionSizePct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CompositeScore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    RealizedR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperTrade", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SwingBacktestResult",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalTrades = table.Column<int>(type: "INTEGER", nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    WinRatePct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AverageR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExpectancyR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ProfitFactor = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MaxDrawdownR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AverageHoldingDays = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ToUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SwingBacktestResult", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_IsSwingUniverse",
                table: "Stock",
                column: "IsSwingUniverse");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTrade_GeneratedAtUtc",
                table: "PaperTrade",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTrade_Status",
                table: "PaperTrade",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTrade_Ticker_GeneratedAtUtc",
                table: "PaperTrade",
                columns: new[] { "Ticker", "GeneratedAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SwingBacktestResult_GeneratedAtUtc",
                table: "SwingBacktestResult",
                column: "GeneratedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperTrade");

            migrationBuilder.DropTable(
                name: "SwingBacktestResult");

            migrationBuilder.DropIndex(
                name: "IX_Stock_IsSwingUniverse",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "IsSwingUniverse",
                table: "Stock");
        }
    }
}
