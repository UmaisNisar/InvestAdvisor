using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestAdvisor.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the auto-scaffolded "DeleteData Profile Id=1" (removing the old singleton seed)
            // was intentionally removed — on a live DB that row is the owner's real profile. Instead
            // we keep it and back-fill its TenantId below.

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "WatchlistItem",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Profile",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Holding",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "DailyRecommendation",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AdviceLog",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Tenant",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenant", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Profile_TenantId",
                table: "Profile",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenant_Email",
                table: "Tenant",
                column: "Email",
                unique: true);

            // Data backfill: only when there is pre-existing (single-tenant) data to inherit.
            // Create the owner tenant and point all existing rows at it. A brand-new DB has no
            // Profile, so nothing is created here — the owner's tenant + default profile are
            // provisioned by TenantContext on first login instead.
            migrationBuilder.Sql(
                "INSERT INTO Tenant (Id, Email, DisplayName, IsOwner, CreatedAtUtc) " +
                "SELECT 1, 'umais.nisar01@gmail.com', 'Owner', 1, '2026-01-01 00:00:00' " +
                "WHERE EXISTS (SELECT 1 FROM Profile);");
            migrationBuilder.Sql("UPDATE Profile SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql("UPDATE Holding SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql("UPDATE WatchlistItem SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql("UPDATE AdviceLog SET TenantId = 1 WHERE TenantId = 0;");
            migrationBuilder.Sql("UPDATE DailyRecommendation SET TenantId = 1 WHERE TenantId = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenant");

            migrationBuilder.DropIndex(
                name: "IX_Profile_TenantId",
                table: "Profile");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WatchlistItem");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Profile");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Holding");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DailyRecommendation");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AdviceLog");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Profile",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.InsertData(
                table: "Profile",
                columns: new[] { "Id", "DriftPctThreshold", "GoalsText", "RebalanceCadenceHours", "RiskTolerance", "SingleDayMovePctThreshold", "SystemPromptOverride", "TimeHorizon", "UpdatedAtUtc" },
                values: new object[] { 1, 5m, "Long-term growth with disciplined rebalancing.", 24, 1, 7m, null, 2, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });
        }
    }
}
