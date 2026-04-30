using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CEMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailConnectionToProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyBudgets");

            migrationBuilder.AddColumn<string>(
                name: "GmailAddress",
                table: "ManagerProfiles",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GmailRefreshToken",
                table: "ManagerProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GmailAddress",
                table: "FinanceProfiles",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GmailRefreshToken",
                table: "FinanceProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GmailAddress",
                table: "DriverProfiles",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GmailRefreshToken",
                table: "DriverProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GmailAddress",
                table: "CEOProfiles",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GmailRefreshToken",
                table: "CEOProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GmailAddress",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "GmailRefreshToken",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "GmailAddress",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "GmailRefreshToken",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "GmailAddress",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "GmailRefreshToken",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "GmailAddress",
                table: "CEOProfiles");

            migrationBuilder.DropColumn(
                name: "GmailRefreshToken",
                table: "CEOProfiles");

            migrationBuilder.CreateTable(
                name: "MonthlyBudgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    Allocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Spent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyBudgets_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyBudgets_BudgetId_Year_Month",
                table: "MonthlyBudgets",
                columns: new[] { "BudgetId", "Year", "Month" },
                unique: true);
        }
    }
}
