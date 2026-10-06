using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FaceReviewStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "MatchDistance",
                table: "Faces",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectedPersonId",
                table: "Faces",
                type: "integer",
                nullable: true);

            // The old Rejected (3) becomes Unknown (0) that remembers the rejected person.
            migrationBuilder.Sql(
                "UPDATE \"Faces\" SET \"RejectedPersonId\" = \"PersonId\", \"PersonId\" = NULL, \"AssignmentState\" = 0 WHERE \"AssignmentState\" = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchDistance",
                table: "Faces");

            migrationBuilder.DropColumn(
                name: "RejectedPersonId",
                table: "Faces");
        }
    }
}
