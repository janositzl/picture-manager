using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FaceClusteredUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClusteredUtc",
                table: "Faces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Faces_FaceModelId_ClusteredUtc",
                table: "Faces",
                columns: new[] { "FaceModelId", "ClusteredUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Faces_FaceModelId_ClusteredUtc",
                table: "Faces");

            migrationBuilder.DropColumn(
                name: "ClusteredUtc",
                table: "Faces");
        }
    }
}
