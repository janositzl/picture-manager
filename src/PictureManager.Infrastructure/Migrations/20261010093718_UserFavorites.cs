using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserFavorites",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFavorites", x => new { x.UserId, x.ImageId });
                    table.ForeignKey(
                        name: "FK_UserFavorites_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFavorites_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFavorites_ImageId",
                table: "UserFavorites",
                column: "ImageId");

            // Favorites were global before accounts existed, so they were the initial admin's (#1). If that account
            // was since deleted, the lowest-id active admin inherits them; with no admin at all they are dropped.
            migrationBuilder.Sql("""
                INSERT INTO "UserFavorites" ("UserId", "ImageId", "CreatedAt")
                SELECT heir."Id", i."Id", now()
                FROM "Images" i
                CROSS JOIN LATERAL (
                    SELECT u."Id" FROM "AppUsers" u
                    WHERE u."Id" = 1 OR (u."Role" = 1 AND u."IsActive")
                    ORDER BY (u."Id" = 1) DESC, u."Id"
                    LIMIT 1
                ) heir
                WHERE i."IsFavorite";
                """);

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Images_Favorites_SortDate_Id\";");
            migrationBuilder.DropIndex(name: "IX_Images_IsFavorite", table: "Images");
            migrationBuilder.DropColumn(name: "IsFavorite", table: "Images");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Images",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Only the initial admin's favorites fit the old global flag.
            migrationBuilder.Sql("""
                UPDATE "Images" SET "IsFavorite" = true
                WHERE "Id" IN (SELECT "ImageId" FROM "UserFavorites" WHERE "UserId" = 1);
                """);

            migrationBuilder.CreateIndex(name: "IX_Images_IsFavorite", table: "Images", column: "IsFavorite");
            migrationBuilder.Sql("CREATE INDEX \"IX_Images_Favorites_SortDate_Id\" ON \"Images\" (\"SortDate\", \"Id\") WHERE \"IsFavorite\";");
            migrationBuilder.DropTable(name: "UserFavorites");
        }
    }
}
