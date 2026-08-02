using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECert.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneCountries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhoneCountries",
                columns: table => new
                {
                    PhoneCountryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CountryName = table.Column<string>(type: "TEXT", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: false),
                    PhoneLength = table.Column<int>(type: "INTEGER", nullable: false),
                    Prefixes = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneCountries", x => x.PhoneCountryId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneCountries");
        }
    }
}
