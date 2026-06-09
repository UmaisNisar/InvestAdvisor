using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingsCsvPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HoldingsCsvPath",
                table: "RuntimeSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "RuntimeSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "HoldingsCsvPath",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoldingsCsvPath",
                table: "RuntimeSettings");
        }
    }
}
