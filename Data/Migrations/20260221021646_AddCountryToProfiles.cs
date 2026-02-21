using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CEMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryToProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "ManagerProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "FinanceProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "DriverProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "CEOProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "CEOProfiles");
        }
    }
}
