using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5RestApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImageRoots_Name",
                table: "ImageRoots");

            migrationBuilder.AddColumn<string>(
                name: "Alias",
                table: "ImageRoots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SortDate",
                table: "Images",
                type: "timestamp with time zone",
                nullable: false,
                computedColumnSql: "COALESCE(\"DateTaken\" AT TIME ZONE 'UTC', \"FileModified\")",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Images_FolderId_SortDate_Id",
                table: "Images",
                columns: new[] { "FolderId", "SortDate", "Id" });

            // Expression / partial indexes EF's fluent API can't express (phase 5 spec, "Data model changes").
            migrationBuilder.Sql("CREATE INDEX \"IX_Images_FolderId_LowerFileName_Id\" ON \"Images\" (\"FolderId\", lower(\"FileName\"), \"Id\");");
            migrationBuilder.Sql("CREATE INDEX \"IX_Images_Favorites_SortDate_Id\" ON \"Images\" (\"SortDate\", \"Id\") WHERE \"IsFavorite\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Albums_OwnerUserId_LowerName\" ON \"Albums\" (\"OwnerUserId\", lower(\"Name\"));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ImageRoots_LowerName\" ON \"ImageRoots\" (lower(\"Name\"));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ImageRoots_ExportSegment\" ON \"ImageRoots\" (lower(COALESCE(\"Alias\", \"Name\")));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ImageRoots_ExportSegment\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ImageRoots_LowerName\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Albums_OwnerUserId_LowerName\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Images_Favorites_SortDate_Id\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Images_FolderId_LowerFileName_Id\";");

            migrationBuilder.DropIndex(
                name: "IX_Images_FolderId_SortDate_Id",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "SortDate",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "Alias",
                table: "ImageRoots");

            migrationBuilder.CreateIndex(
                name: "IX_ImageRoots_Name",
                table: "ImageRoots",
                column: "Name",
                unique: true);
        }
    }
}
