using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECert.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceCancelRestoreFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestoredAt",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestoredBy",
                table: "Invoices",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RestoredAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RestoredBy",
                table: "Invoices");
        }
    }
}
