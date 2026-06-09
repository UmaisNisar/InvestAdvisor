using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingsCsvUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HoldingsCsvUrl",
                table: "RuntimeSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "RuntimeSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "HoldingsCsvUrl",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoldingsCsvUrl",
                table: "RuntimeSettings");
        }
    }
}
