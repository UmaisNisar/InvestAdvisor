using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetClassesAndMomentum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Beta",
                table: "StockMetric",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MomentumLong",
                table: "StockMetric",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MomentumShort",
                table: "StockMetric",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssetClass",
                table: "Stock",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Stock",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_AssetClass",
                table: "Stock",
                column: "AssetClass");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stock_AssetClass",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "Beta",
                table: "StockMetric");

            migrationBuilder.DropColumn(
                name: "MomentumLong",
                table: "StockMetric");

            migrationBuilder.DropColumn(
                name: "MomentumShort",
                table: "StockMetric");

            migrationBuilder.DropColumn(
                name: "AssetClass",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Stock");
        }
    }
}
