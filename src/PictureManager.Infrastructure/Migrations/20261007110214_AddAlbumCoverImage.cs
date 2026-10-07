using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlbumCoverImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CoverImageId",
                table: "Albums",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverImageId",
                table: "Albums");
        }
    }
}
