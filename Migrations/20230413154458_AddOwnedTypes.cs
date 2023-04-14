using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hcefcustom.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnedTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Video_Id",
                table: "Lessons",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Video_Thumbnail_Blurhash",
                table: "Lessons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Video_Thumbnail_Id",
                table: "Lessons",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Video_Id",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "Video_Thumbnail_Blurhash",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "Video_Thumbnail_Id",
                table: "Lessons");
        }
    }
}
