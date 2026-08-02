using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECert.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedDate",
                table: "Registrations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReopenedDate",
                table: "Registrations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedDate",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "ReopenedDate",
                table: "Registrations");
        }
    }
}
