using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CEMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitAddressIntoSeparateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "CEOProfiles");

            migrationBuilder.AddColumn<string>(
                name: "Barangay",
                table: "ManagerProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "ManagerProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "ManagerProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "ManagerProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "ManagerProfiles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Barangay",
                table: "FinanceProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "FinanceProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "FinanceProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "FinanceProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "FinanceProfiles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Barangay",
                table: "DriverProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "DriverProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "DriverProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "DriverProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "DriverProfiles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Barangay",
                table: "CEOProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "CEOProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "CEOProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "CEOProfiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "CEOProfiles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Barangay",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "City",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "Barangay",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "City",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "Barangay",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "City",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "Barangay",
                table: "CEOProfiles");

            migrationBuilder.DropColumn(
                name: "City",
                table: "CEOProfiles");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "CEOProfiles");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "CEOProfiles");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "CEOProfiles");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "ManagerProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "FinanceProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "DriverProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "CEOProfiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }
    }
}
