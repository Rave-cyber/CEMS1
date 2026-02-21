using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CEMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAddressContactNumberToProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "ManagerProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactNumber",
                table: "ManagerProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "FinanceProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactNumber",
                table: "FinanceProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "DriverProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactNumber",
                table: "DriverProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "CEOProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactNumber",
                table: "CEOProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "ContactNumber",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "ContactNumber",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "ContactNumber",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "CEOProfiles");

            migrationBuilder.DropColumn(
                name: "ContactNumber",
                table: "CEOProfiles");
        }
    }
}
