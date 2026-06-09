using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvicePositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParsedPositionsJson",
                table: "AdviceLog",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParsedPositionsJson",
                table: "AdviceLog");
        }
    }
}
