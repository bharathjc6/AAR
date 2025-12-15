using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AAR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSymbolAndConfidenceToReviewFinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "ReviewFindings",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Symbol",
                table: "ReviewFindings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "ReviewFindings");

            migrationBuilder.DropColumn(
                name: "Symbol",
                table: "ReviewFindings");
        }
    }
}
