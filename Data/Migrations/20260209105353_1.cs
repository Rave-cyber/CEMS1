using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CEMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class _1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CEOApproved",
                table: "ExpenseReports",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Reimbursed",
                table: "ExpenseReports",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CEOApproved",
                table: "ExpenseReports");

            migrationBuilder.DropColumn(
                name: "Reimbursed",
                table: "ExpenseReports");
        }
    }
}
