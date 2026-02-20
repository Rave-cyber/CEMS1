using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CEMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripContextToExpenseReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TripDays",
                table: "ExpenseReports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TripEnd",
                table: "ExpenseReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TripStart",
                table: "ExpenseReports",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TripDays",
                table: "ExpenseReports");

            migrationBuilder.DropColumn(
                name: "TripEnd",
                table: "ExpenseReports");

            migrationBuilder.DropColumn(
                name: "TripStart",
                table: "ExpenseReports");
        }
    }
}
