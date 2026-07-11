using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParallelScope.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FileSystemEntries_IsFolder_Name",
                table: "FileSystemEntries");

            migrationBuilder.DropIndex(
                name: "IX_FileSystemEntries_Name",
                table: "FileSystemEntries");

            migrationBuilder.DropIndex(
                name: "IX_FileSystemEntries_ParentPath_Name",
                table: "FileSystemEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_IsFolder_Name",
                table: "FileSystemEntries",
                columns: new[] { "IsFolder", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_Name",
                table: "FileSystemEntries",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_ParentPath_Name",
                table: "FileSystemEntries",
                columns: new[] { "ParentPath", "Name" });
        }
    }
}
