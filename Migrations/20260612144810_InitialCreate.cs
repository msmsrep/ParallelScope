using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParallelScope.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileSystemEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentPath = table.Column<string>(type: "TEXT", nullable: false),
                    FullPath = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsFolder = table.Column<bool>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileSystemEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_FullPath",
                table: "FileSystemEntries",
                column: "FullPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_ParentPath",
                table: "FileSystemEntries",
                column: "ParentPath");

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_ParentPath_Name",
                table: "FileSystemEntries",
                columns: new[] { "ParentPath", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileSystemEntries");
        }
    }
}
