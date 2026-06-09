using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenerFactorWeights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WeightAnalyst",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeightGrowth",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeightInsider",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeightMomentum",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeightQuality",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeightValuation",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "RuntimeSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "WeightAnalyst", "WeightGrowth", "WeightInsider", "WeightMomentum", "WeightQuality", "WeightValuation" },
                values: new object[] { 20, 25, 10, 15, 10, 20 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeightAnalyst",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WeightGrowth",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WeightInsider",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WeightMomentum",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WeightQuality",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "WeightValuation",
                table: "RuntimeSettings");
        }
    }
}
