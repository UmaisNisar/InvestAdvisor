using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnifyStrategyTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SwingWatchItem_GeneratedAtUtc",
                table: "SwingWatchItem");

            migrationBuilder.DropIndex(
                name: "IX_PaperTrade_GeneratedAtUtc",
                table: "PaperTrade");

            migrationBuilder.DropIndex(
                name: "IX_PaperTrade_Status",
                table: "PaperTrade");

            migrationBuilder.DropIndex(
                name: "IX_PaperTrade_Ticker_GeneratedAtUtc",
                table: "PaperTrade");

            migrationBuilder.AddColumn<int>(
                name: "Strategy",
                table: "SwingWatchItem",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "AtrPercent",
                table: "PaperTrade",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BreakoutStrength",
                table: "PaperTrade",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Strategy",
                table: "PaperTrade",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetGainPct",
                table: "PaperTrade",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "BacktestResult",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Strategy = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_BacktestResult", x => x.Id);
                });

            // Carry the old per-strategy rows into the unified tables before dropping them.
            // Strategy: 0 = Swing, 1 = Momentum (InvestAdvisor.Core.Trading.StrategyKind).
            migrationBuilder.Sql("""
                INSERT INTO BacktestResult (Strategy, GeneratedAtUtc, TotalTrades, Wins, Losses, WinRatePct, AverageR, ExpectancyR, ProfitFactor, MaxDrawdownR, AverageHoldingDays, FromUtc, ToUtc)
                SELECT 0, GeneratedAtUtc, TotalTrades, Wins, Losses, WinRatePct, AverageR, ExpectancyR, ProfitFactor, MaxDrawdownR, AverageHoldingDays, FromUtc, ToUtc FROM SwingBacktestResult;
                INSERT INTO BacktestResult (Strategy, GeneratedAtUtc, TotalTrades, Wins, Losses, WinRatePct, AverageR, ExpectancyR, ProfitFactor, MaxDrawdownR, AverageHoldingDays, FromUtc, ToUtc)
                SELECT 1, GeneratedAtUtc, TotalTrades, Wins, Losses, WinRatePct, AverageR, ExpectancyR, ProfitFactor, MaxDrawdownR, AverageHoldingDays, FromUtc, ToUtc FROM MomentumBacktestResult;
                INSERT INTO PaperTrade (Strategy, Ticker, Name, GeneratedAtUtc, EntryLow, EntryHigh, EntryReference, StopLoss, Target, RewardRiskRatio, HoldingDays, PositionSizePct, TargetGainPct, CompositeScore, Rationale, Kind, RelativeVolume, AtrPercent, BreakoutStrength, Status)
                SELECT 1, Ticker, Name, GeneratedAtUtc, EntryLow, EntryHigh, EntryReference, StopLoss, Target, RewardRiskRatio, HoldingDays, PositionSizePct, TargetGainPct, CompositeScore, Rationale, Kind, RelativeVolume, AtrPercent, BreakoutStrength, 0 FROM MomentumCandidate;
                UPDATE PaperTrade SET TargetGainPct = CASE WHEN EntryReference = 0 THEN 0 ELSE ROUND((Target - EntryReference) / EntryReference * 100, 2) END WHERE Strategy = 0;
                """);

            migrationBuilder.DropTable(name: "MomentumBacktestResult");
            migrationBuilder.DropTable(name: "MomentumCandidate");
            migrationBuilder.DropTable(name: "SwingBacktestResult");

            migrationBuilder.CreateIndex(
                name: "IX_SwingWatchItem_Strategy_GeneratedAtUtc",
                table: "SwingWatchItem",
                columns: new[] { "Strategy", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperTrade_Strategy_GeneratedAtUtc",
                table: "PaperTrade",
                columns: new[] { "Strategy", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperTrade_Strategy_Status",
                table: "PaperTrade",
                columns: new[] { "Strategy", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperTrade_Strategy_Ticker_GeneratedAtUtc",
                table: "PaperTrade",
                columns: new[] { "Strategy", "Ticker", "GeneratedAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BacktestResult_Strategy_GeneratedAtUtc",
                table: "BacktestResult",
                columns: new[] { "Strategy", "GeneratedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BacktestResult");

            migrationBuilder.DropIndex(
                name: "IX_SwingWatchItem_Strategy_GeneratedAtUtc",
                table: "SwingWatchItem");

            migrationBuilder.DropIndex(
                name: "IX_PaperTrade_Strategy_GeneratedAtUtc",
                table: "PaperTrade");

            migrationBuilder.DropIndex(
                name: "IX_PaperTrade_Strategy_Status",
                table: "PaperTrade");

            migrationBuilder.DropIndex(
                name: "IX_PaperTrade_Strategy_Ticker_GeneratedAtUtc",
                table: "PaperTrade");

            migrationBuilder.DropColumn(
                name: "Strategy",
                table: "SwingWatchItem");

            migrationBuilder.DropColumn(
                name: "AtrPercent",
                table: "PaperTrade");

            migrationBuilder.DropColumn(
                name: "BreakoutStrength",
                table: "PaperTrade");

            migrationBuilder.DropColumn(
                name: "Strategy",
                table: "PaperTrade");

            migrationBuilder.DropColumn(
                name: "TargetGainPct",
                table: "PaperTrade");

            migrationBuilder.CreateTable(
                name: "MomentumBacktestResult",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AverageHoldingDays = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AverageR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExpectancyR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDrawdownR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ProfitFactor = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ToUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalTrades = table.Column<int>(type: "INTEGER", nullable: false),
                    WinRatePct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false)
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
                    AtrPercent = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    BreakoutStrength = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CompositeScore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EntryHigh = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EntryLow = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EntryReference = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HoldingDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PositionSizePct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    RelativeVolume = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    RewardRiskRatio = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    StopLoss = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Target = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TargetGainPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MomentumCandidate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SwingBacktestResult",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AverageHoldingDays = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AverageR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExpectancyR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDrawdownR = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ProfitFactor = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ToUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalTrades = table.Column<int>(type: "INTEGER", nullable: false),
                    WinRatePct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SwingBacktestResult", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SwingWatchItem_GeneratedAtUtc",
                table: "SwingWatchItem",
                column: "GeneratedAtUtc");

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

            migrationBuilder.CreateIndex(
                name: "IX_SwingBacktestResult_GeneratedAtUtc",
                table: "SwingBacktestResult",
                column: "GeneratedAtUtc");
        }
    }
}
