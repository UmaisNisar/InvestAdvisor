using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanupSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertDelivery");

            migrationBuilder.DropTable(
                name: "StockAnalysis");

            migrationBuilder.DropIndex(
                name: "IX_WatchlistItem_Ticker_AssetClass",
                table: "WatchlistItem");

            migrationBuilder.DropIndex(
                name: "IX_Stock_AssetClass",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Stock_IsMomentumUniverse",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Stock_IsSwingUniverse",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Holding_Ticker_AccountType",
                table: "Holding");

            migrationBuilder.DropIndex(
                name: "IX_DailyRecommendation_GeneratedAtUtc",
                table: "DailyRecommendation");

            migrationBuilder.DropIndex(
                name: "IX_AdviceLog_ReplayOfAdviceLogId",
                table: "AdviceLog");

            migrationBuilder.DropIndex(
                name: "IX_AdviceLog_TimestampUtc",
                table: "AdviceLog");

            migrationBuilder.DropColumn(
                name: "StocksJson",
                table: "DailyRecommendation");

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistItem_TenantId_Ticker_AssetClass",
                table: "WatchlistItem",
                columns: new[] { "TenantId", "Ticker", "AssetClass" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_IsActive_AssetClass",
                table: "Stock",
                columns: new[] { "IsActive", "AssetClass" });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_IsActive_IsMomentumUniverse",
                table: "Stock",
                columns: new[] { "IsActive", "IsMomentumUniverse" });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_IsActive_IsSwingUniverse",
                table: "Stock",
                columns: new[] { "IsActive", "IsSwingUniverse" });

            migrationBuilder.CreateIndex(
                name: "IX_Holding_TenantId_Ticker_AccountType",
                table: "Holding",
                columns: new[] { "TenantId", "Ticker", "AccountType" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRecommendation_TenantId_GeneratedAtUtc",
                table: "DailyRecommendation",
                columns: new[] { "TenantId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdviceLog_TenantId_TimestampUtc",
                table: "AdviceLog",
                columns: new[] { "TenantId", "TimestampUtc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchlistItem_TenantId_Ticker_AssetClass",
                table: "WatchlistItem");

            migrationBuilder.DropIndex(
                name: "IX_Stock_IsActive_AssetClass",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Stock_IsActive_IsMomentumUniverse",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Stock_IsActive_IsSwingUniverse",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Holding_TenantId_Ticker_AccountType",
                table: "Holding");

            migrationBuilder.DropIndex(
                name: "IX_DailyRecommendation_TenantId_GeneratedAtUtc",
                table: "DailyRecommendation");

            migrationBuilder.DropIndex(
                name: "IX_AdviceLog_TenantId_TimestampUtc",
                table: "AdviceLog");

            migrationBuilder.AddColumn<string>(
                name: "StocksJson",
                table: "DailyRecommendation",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AlertDelivery",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdviceLogId = table.Column<long>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertDelivery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertDelivery_AdviceLog_AdviceLogId",
                        column: x => x.AdviceLogId,
                        principalTable: "AdviceLog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockAnalysis",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BearishFactorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    BullishFactorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CompositeScore = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Conviction = table.Column<int>(type: "INTEGER", nullable: false),
                    ConvictionLabel = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    KeyRisksJson = table.Column<string>(type: "TEXT", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Thesis = table.Column<string>(type: "TEXT", nullable: false),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAnalysis", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistItem_Ticker_AssetClass",
                table: "WatchlistItem",
                columns: new[] { "Ticker", "AssetClass" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_AssetClass",
                table: "Stock",
                column: "AssetClass");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_IsMomentumUniverse",
                table: "Stock",
                column: "IsMomentumUniverse");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_IsSwingUniverse",
                table: "Stock",
                column: "IsSwingUniverse");

            migrationBuilder.CreateIndex(
                name: "IX_Holding_Ticker_AccountType",
                table: "Holding",
                columns: new[] { "Ticker", "AccountType" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRecommendation_GeneratedAtUtc",
                table: "DailyRecommendation",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdviceLog_ReplayOfAdviceLogId",
                table: "AdviceLog",
                column: "ReplayOfAdviceLogId");

            migrationBuilder.CreateIndex(
                name: "IX_AdviceLog_TimestampUtc",
                table: "AdviceLog",
                column: "TimestampUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AlertDelivery_AdviceLogId_Channel",
                table: "AlertDelivery",
                columns: new[] { "AdviceLogId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertDelivery_Status",
                table: "AlertDelivery",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StockAnalysis_Ticker_GeneratedAtUtc",
                table: "StockAnalysis",
                columns: new[] { "Ticker", "GeneratedAtUtc" });
        }
    }
}
