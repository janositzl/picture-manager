using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FolderScanStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExcluded",
                table: "Folders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LastScanFileCount",
                table: "Folders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastScannedAt",
                table: "Folders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScanStatus",
                table: "Folders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Folders_ScanStatus",
                table: "Folders",
                column: "ScanStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Folders_ScanStatus",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "IsExcluded",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "LastScanFileCount",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "LastScannedAt",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "ScanStatus",
                table: "Folders");
        }
    }
}
