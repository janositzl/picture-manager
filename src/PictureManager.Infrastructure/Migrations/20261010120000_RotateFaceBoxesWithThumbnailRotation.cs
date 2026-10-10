using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PictureManager.Infrastructure.Persistence;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <summary>
    /// Face boxes now live in the displayed frame (EXIF + ThumbnailRotation). Boxes detected before this change are
    /// still in the EXIF frame, so images the user already rotated get their boxes turned to match.
    /// </summary>
    [DbContext(typeof(PictureManagerDbContext))]
    [Migration("20261010120000_RotateFaceBoxesWithThumbnailRotation")]
    public partial class RotateFaceBoxesWithThumbnailRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // In an UPDATE every right-hand side reads the old row, so the swaps below are safe.
            migrationBuilder.Sql("""
                UPDATE "Faces" f SET "X" = 1 - f."Y" - f."Height", "Y" = f."X", "Width" = f."Height", "Height" = f."Width"
                FROM "Images" i WHERE i."Id" = f."ImageId" AND i."ThumbnailRotation" = 90;
                UPDATE "Faces" f SET "X" = 1 - f."X" - f."Width", "Y" = 1 - f."Y" - f."Height"
                FROM "Images" i WHERE i."Id" = f."ImageId" AND i."ThumbnailRotation" = 180;
                UPDATE "Faces" f SET "X" = f."Y", "Y" = 1 - f."X" - f."Width", "Width" = f."Height", "Height" = f."Width"
                FROM "Images" i WHERE i."Id" = f."ImageId" AND i."ThumbnailRotation" = 270;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Faces" f SET "X" = f."Y", "Y" = 1 - f."X" - f."Width", "Width" = f."Height", "Height" = f."Width"
                FROM "Images" i WHERE i."Id" = f."ImageId" AND i."ThumbnailRotation" = 90;
                UPDATE "Faces" f SET "X" = 1 - f."X" - f."Width", "Y" = 1 - f."Y" - f."Height"
                FROM "Images" i WHERE i."Id" = f."ImageId" AND i."ThumbnailRotation" = 180;
                UPDATE "Faces" f SET "X" = 1 - f."Y" - f."Height", "Y" = f."X", "Width" = f."Height", "Height" = f."Width"
                FROM "Images" i WHERE i."Id" = f."ImageId" AND i."ThumbnailRotation" = 270;
                """);
        }
    }
}
