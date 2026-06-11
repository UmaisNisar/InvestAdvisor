using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmProviderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LlmCustomBaseUrl",
                table: "RuntimeSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LlmModel",
                table: "RuntimeSettings",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LlmProvider",
                table: "RuntimeSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LlmRoutineModel",
                table: "RuntimeSettings",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "RuntimeSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "LlmCustomBaseUrl", "LlmModel", "LlmProvider", "LlmRoutineModel" },
                values: new object[] { null, "gemini-2.5-flash", "gemini", "gemini-2.5-flash-lite" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LlmCustomBaseUrl",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LlmModel",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LlmProvider",
                table: "RuntimeSettings");

            migrationBuilder.DropColumn(
                name: "LlmRoutineModel",
                table: "RuntimeSettings");
        }
    }
}
