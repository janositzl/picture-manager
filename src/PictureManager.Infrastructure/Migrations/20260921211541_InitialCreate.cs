using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExcludedFolderNames = table.Column<string>(type: "jsonb", nullable: false),
                    ExcludedExtensions = table.Column<string>(type: "jsonb", nullable: false),
                    IncludedExtensions = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ZitadelSubjectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageRoots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MountPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageRoots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Albums",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Albums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Albums_AppUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Folders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RootId = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Folders_Folders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Folders_ImageRoots_RootId",
                        column: x => x.RootId,
                        principalTable: "ImageRoots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Images",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FolderId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Extension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PerceptualHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FileModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    Orientation = table.Column<int>(type: "integer", nullable: true),
                    DateTaken = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CameraMake = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CameraModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LensModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    RawMetadata = table.Column<string>(type: "jsonb", nullable: true),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    IndexState = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MissingSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Images_Folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScanJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RootFolderId = table.Column<int>(type: "integer", nullable: true),
                    IsRecursive = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FoldersScanned = table.Column<int>(type: "integer", nullable: false),
                    FilesFound = table.Column<int>(type: "integer", nullable: false),
                    FilesEnriched = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanJobs_Folders_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AlbumImages",
                columns: table => new
                {
                    AlbumId = table.Column<int>(type: "integer", nullable: false),
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumImages", x => new { x.AlbumId, x.ImageId });
                    table.ForeignKey(
                        name: "FK_AlbumImages_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AlbumImages_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Id", "ExcludedExtensions", "ExcludedFolderNames", "IncludedExtensions" },
                values: new object[] { 1, "[\".heic\"]", "[\"raw\",\"backup\",\"@eaDir\"]", null });

            migrationBuilder.InsertData(
                table: "AppUsers",
                columns: new[] { "Id", "DisplayName", "Role", "ZitadelSubjectId" },
                values: new object[] { 1, "System", 0, null });

            migrationBuilder.CreateIndex(
                name: "IX_AlbumImages_ImageId",
                table: "AlbumImages",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_Albums_OwnerUserId",
                table: "Albums",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_ZitadelSubjectId",
                table: "AppUsers",
                column: "ZitadelSubjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Folders_ParentId",
                table: "Folders",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_RelativePath",
                table: "Folders",
                column: "RelativePath");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_RootId",
                table: "Folders",
                column: "RootId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageRoots_Name",
                table: "ImageRoots",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Images_ContentHash",
                table: "Images",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_Images_FolderId",
                table: "Images",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_IsFavorite",
                table: "Images",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobs_RootFolderId",
                table: "ScanJobs",
                column: "RootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobs_Status",
                table: "ScanJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbumImages");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "ScanJobs");

            migrationBuilder.DropTable(
                name: "Albums");

            migrationBuilder.DropTable(
                name: "Images");

            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "Folders");

            migrationBuilder.DropTable(
                name: "ImageRoots");
        }
    }
}
