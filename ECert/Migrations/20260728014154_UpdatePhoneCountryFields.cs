using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECert.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePhoneCountryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PhoneLength",
                table: "PhoneCountries",
                newName: "MinPhoneLength");

            migrationBuilder.AddColumn<int>(
                name: "MaxPhoneLength",
                table: "PhoneCountries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxPhoneLength",
                table: "PhoneCountries");

            migrationBuilder.RenameColumn(
                name: "MinPhoneLength",
                table: "PhoneCountries",
                newName: "PhoneLength");
        }
    }
}
