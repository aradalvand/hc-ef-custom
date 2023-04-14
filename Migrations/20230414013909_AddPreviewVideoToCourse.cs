using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace hcefcustom.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewVideoToCourse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreviewVideo_Id",
                table: "Courses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "PreviewVideo_Thumbnail_Blurhash",
                table: "Courses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PreviewVideo_Thumbnail_Id",
                table: "Courses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviewVideo_Id",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "PreviewVideo_Thumbnail_Blurhash",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "PreviewVideo_Thumbnail_Id",
                table: "Courses");
        }
    }
}
