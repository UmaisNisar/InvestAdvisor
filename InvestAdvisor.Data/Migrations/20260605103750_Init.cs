using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdviceLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                    TriggerDetail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StructuredInputJson = table.Column<string>(type: "TEXT", nullable: false),
                    SystemPromptUsed = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    RawResponseText = table.Column<string>(type: "TEXT", nullable: false),
                    ParsedSummary = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ParsedFlagsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ParsedDriftAlertsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ParsedConsiderationsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    ParseFallbackUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReplayOfAdviceLogId = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdviceLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Holding",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    AssetClass = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    AvgCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AccountType = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetAllocationPct = table.Column<decimal>(type: "TEXT", precision: 8, scale: 4, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holding", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NewsItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Headline = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsItem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PriceSnapshot",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AssetClass = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    PreviousClose = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    PercentChange = table.Column<decimal>(type: "TEXT", precision: 8, scale: 4, nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceSnapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Profile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalsText = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    RiskTolerance = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeHorizon = table.Column<int>(type: "INTEGER", nullable: false),
                    DriftPctThreshold = table.Column<decimal>(type: "TEXT", precision: 8, scale: 4, nullable: false),
                    SingleDayMovePctThreshold = table.Column<decimal>(type: "TEXT", precision: 8, scale: 4, nullable: false),
                    RebalanceCadenceHours = table.Column<int>(type: "INTEGER", nullable: false),
                    SystemPromptOverride = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    TickIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MarketHoursOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MaxRunsPerDay = table.Column<int>(type: "INTEGER", nullable: false),
                    MinSecondsBetweenRuns = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxSnapshotAgeForTriggerSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MinPriceFreshnessSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SmtpHost = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SmtpPort = table.Column<int>(type: "INTEGER", nullable: false),
                    SmtpFrom = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SmtpTo = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SmtpEnableSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchlistItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AssetClass = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PriceTargetLow = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    PriceTargetHigh = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchlistItem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlertDelivery",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdviceLogId = table.Column<long>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false)
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

            migrationBuilder.InsertData(
                table: "Profile",
                columns: new[] { "Id", "DriftPctThreshold", "GoalsText", "RebalanceCadenceHours", "RiskTolerance", "SingleDayMovePctThreshold", "SystemPromptOverride", "TimeHorizon", "UpdatedAtUtc" },
                values: new object[] { 1, 5m, "Long-term growth with disciplined rebalancing.", 24, 1, 7m, null, 2, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "RuntimeSettings",
                columns: new[] { "Id", "EmailEnabled", "MarketHoursOnly", "MaxRunsPerDay", "MaxSnapshotAgeForTriggerSeconds", "MinPriceFreshnessSeconds", "MinSecondsBetweenRuns", "SmtpEnableSsl", "SmtpFrom", "SmtpHost", "SmtpPort", "SmtpTo", "TickIntervalSeconds", "TimeZoneId", "UpdatedAtUtc" },
                values: new object[] { 1, false, true, 24, 600, 60, 900, true, null, null, 587, null, 300, "America/New_York", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

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
                name: "IX_Holding_Ticker_AccountType",
                table: "Holding",
                columns: new[] { "Ticker", "AccountType" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsItem_Ticker_PublishedAtUtc",
                table: "NewsItem",
                columns: new[] { "Ticker", "PublishedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_NewsItem_Url",
                table: "NewsItem",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceSnapshot_Ticker_FetchedAtUtc",
                table: "PriceSnapshot",
                columns: new[] { "Ticker", "FetchedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistItem_Ticker_AssetClass",
                table: "WatchlistItem",
                columns: new[] { "Ticker", "AssetClass" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertDelivery");

            migrationBuilder.DropTable(
                name: "Holding");

            migrationBuilder.DropTable(
                name: "NewsItem");

            migrationBuilder.DropTable(
                name: "PriceSnapshot");

            migrationBuilder.DropTable(
                name: "Profile");

            migrationBuilder.DropTable(
                name: "RuntimeSettings");

            migrationBuilder.DropTable(
                name: "WatchlistItem");

            migrationBuilder.DropTable(
                name: "AdviceLog");
        }
    }
}
