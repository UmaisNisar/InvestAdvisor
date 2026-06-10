using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsSentiment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WeightSentiment",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "NewsItem",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SentimentLabel",
                table: "NewsItem",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SentimentScore",
                table: "NewsItem",
                type: "decimal(4,3)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentimentScoredAtUtc",
                table: "NewsItem",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SentimentRun",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ItemsScored = table.Column<int>(type: "INTEGER", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentimentRun", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "RuntimeSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "WeightSentiment",
                value: 10);

            migrationBuilder.CreateIndex(
                name: "IX_NewsItem_SentimentScoredAtUtc",
                table: "NewsItem",
                column: "SentimentScoredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SentimentRun_GeneratedAtUtc",
                table: "SentimentRun",
                column: "GeneratedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentimentRun");

            migrationBuilder.DropIndex(
                name: "IX_NewsItem_SentimentScoredAtUtc",
                table: "NewsItem");

            migrationBuilder.DropColumn(
                name: "WeightSentiment",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "NewsItem");

            migrationBuilder.DropColumn(
                name: "SentimentLabel",
                table: "NewsItem");

            migrationBuilder.DropColumn(
                name: "SentimentScore",
                table: "NewsItem");

            migrationBuilder.DropColumn(
                name: "SentimentScoredAtUtc",
                table: "NewsItem");
        }
    }
}
