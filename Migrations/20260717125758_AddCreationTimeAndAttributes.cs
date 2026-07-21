using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParallelScope.Migrations
{
    /// <inheritdoc />
    public partial class AddCreationTimeAndAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attributes",
                table: "FileSystemEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreationTimeUtc",
                table: "FileSystemEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attributes",
                table: "FileSystemEntries");

            migrationBuilder.DropColumn(
                name: "CreationTimeUtc",
                table: "FileSystemEntries");
        }
    }
}
