using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDarkModeSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DarkMode",
                table: "RuntimeSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "RuntimeSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DarkMode",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DarkMode",
                table: "RuntimeSettings");
        }
    }
}
