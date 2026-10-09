using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PictureManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_ZitadelSubjectId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "ZitadelSubjectId",
                table: "AppUsers");

            migrationBuilder.AddColumn<bool>(
                name: "CanRunFolderActions",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUsername",
                table: "AppUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "AppUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityStamp",
                table: "AppUsers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "AppUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "AppUsers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CanRunFolderActions", "CreatedAt", "DisplayName", "IsActive", "LastLoginAt", "MustChangePassword", "NormalizedUsername", "PasswordHash", "Role", "SecurityStamp", "Username" },
                values: new object[] { false, new DateTime(2026, 9, 21, 0, 0, 0, 0, DateTimeKind.Utc), "Administrator", true, null, false, "admin", null, 1, new Guid("6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b"), "admin" });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_NormalizedUsername",
                table: "AppUsers",
                column: "NormalizedUsername",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_NormalizedUsername",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "CanRunFolderActions",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "NormalizedUsername",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "AppUsers");

            migrationBuilder.AddColumn<string>(
                name: "ZitadelSubjectId",
                table: "AppUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppUsers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DisplayName", "Role", "ZitadelSubjectId" },
                values: new object[] { "System", 0, null });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_ZitadelSubjectId",
                table: "AppUsers",
                column: "ZitadelSubjectId",
                unique: true);
        }
    }
}
