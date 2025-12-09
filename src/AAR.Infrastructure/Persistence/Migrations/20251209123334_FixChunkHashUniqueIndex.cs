using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AAR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixChunkHashUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chunks_ChunkHash",
                table: "Chunks");

            migrationBuilder.CreateIndex(
                name: "IX_Chunks_ProjectId_ChunkHash",
                table: "Chunks",
                columns: new[] { "ProjectId", "ChunkHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chunks_ProjectId_ChunkHash",
                table: "Chunks");

            migrationBuilder.CreateIndex(
                name: "IX_Chunks_ChunkHash",
                table: "Chunks",
                column: "ChunkHash",
                unique: true);
        }
    }
}
