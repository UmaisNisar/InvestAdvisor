using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenerEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalystRating",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Period = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StrongBuy = table.Column<int>(type: "INTEGER", nullable: false),
                    Buy = table.Column<int>(type: "INTEGER", nullable: false),
                    Hold = table.Column<int>(type: "INTEGER", nullable: false),
                    Sell = table.Column<int>(type: "INTEGER", nullable: false),
                    StrongSell = table.Column<int>(type: "INTEGER", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalystRating", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsiderTrade",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Change = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Shares = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FilingDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TransactionCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    IsDerivative = table.Column<bool>(type: "INTEGER", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderTrade", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stock",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Sector = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stock", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockAnalysis",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompositeScore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Thesis = table.Column<string>(type: "TEXT", nullable: false),
                    BullishFactorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    BearishFactorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    KeyRisksJson = table.Column<string>(type: "TEXT", nullable: false),
                    Conviction = table.Column<int>(type: "INTEGER", nullable: false),
                    ConvictionLabel = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAnalysis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockMetric",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MarketCap = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    PeRatio = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    RevenueGrowthPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    EpsGrowthPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    DebtToEquity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    PriceToFreeCashFlow = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    RawJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMetric", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalystRating_Ticker_Period",
                table: "AnalystRating",
                columns: new[] { "Ticker", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTrade_Ticker_FilingDate",
                table: "InsiderTrade",
                columns: new[] { "Ticker", "FilingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Ticker",
                table: "Stock",
                column: "Ticker",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAnalysis_Ticker_GeneratedAtUtc",
                table: "StockAnalysis",
                columns: new[] { "Ticker", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMetric_Ticker_FetchedAtUtc",
                table: "StockMetric",
                columns: new[] { "Ticker", "FetchedAtUtc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalystRating");

            migrationBuilder.DropTable(
                name: "InsiderTrade");

            migrationBuilder.DropTable(
                name: "Stock");

            migrationBuilder.DropTable(
                name: "StockAnalysis");

            migrationBuilder.DropTable(
                name: "StockMetric");
        }
    }
}
