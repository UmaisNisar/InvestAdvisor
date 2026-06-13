using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperTradeSignalContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PullbackPct",
                table: "PaperTrade",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RegimeDistancePct",
                table: "PaperTrade",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RelativeVolume",
                table: "PaperTrade",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SignalRsi",
                table: "PaperTrade",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PullbackPct",
                table: "PaperTrade");

            migrationBuilder.DropColumn(
                name: "RegimeDistancePct",
                table: "PaperTrade");

            migrationBuilder.DropColumn(
                name: "RelativeVolume",
                table: "PaperTrade");

            migrationBuilder.DropColumn(
                name: "SignalRsi",
                table: "PaperTrade");
        }
    }
}
