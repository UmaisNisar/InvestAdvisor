using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMomentumModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMomentumUniverse",
                table: "Stock",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MomentumRiskLevel",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MomentumBacktestResult",
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
                    table.PrimaryKey("PK_MomentumBacktestResult", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MomentumCandidate",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EntryLow = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EntryHigh = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EntryReference = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    StopLoss = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Target = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RewardRiskRatio = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    HoldingDays = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionSizePct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TargetGainPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CompositeScore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    AtrPercent = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    BreakoutStrength = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    RelativeVolume = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MomentumCandidate", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "RuntimeSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "MomentumRiskLevel",
                value: 2);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_IsMomentumUniverse",
                table: "Stock",
                column: "IsMomentumUniverse");

            migrationBuilder.CreateIndex(
                name: "IX_MomentumBacktestResult_GeneratedAtUtc",
                table: "MomentumBacktestResult",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MomentumCandidate_GeneratedAtUtc",
                table: "MomentumCandidate",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MomentumCandidate_Ticker_GeneratedAtUtc",
                table: "MomentumCandidate",
                columns: new[] { "Ticker", "GeneratedAtUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MomentumBacktestResult");

            migrationBuilder.DropTable(
                name: "MomentumCandidate");

            migrationBuilder.DropIndex(
                name: "IX_Stock_IsMomentumUniverse",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "IsMomentumUniverse",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "MomentumRiskLevel",
                table: "RuntimeSettings");
        }
    }
}
