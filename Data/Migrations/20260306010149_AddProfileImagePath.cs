using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CEMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileImagePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfileImagePath",
                table: "ManagerProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImagePath",
                table: "FinanceProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImagePath",
                table: "DriverProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImagePath",
                table: "CEOProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileImagePath",
                table: "ManagerProfiles");

            migrationBuilder.DropColumn(
                name: "ProfileImagePath",
                table: "FinanceProfiles");

            migrationBuilder.DropColumn(
                name: "ProfileImagePath",
                table: "DriverProfiles");

            migrationBuilder.DropColumn(
                name: "ProfileImagePath",
                table: "CEOProfiles");
        }
    }
}
