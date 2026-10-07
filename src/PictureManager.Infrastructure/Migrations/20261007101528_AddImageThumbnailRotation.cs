using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImageThumbnailRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ThumbnailRotation",
                table: "Images",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailRotation",
                table: "Images");
        }
    }
}
