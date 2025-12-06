using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AAR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RequestCount = table.Column<long>(type: "INTEGER", nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    GitRepoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    AnalysisStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AnalysisCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalLinesOfCode = table.Column<int>(type: "INTEGER", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    IsAnalyzed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Metrics_LinesOfCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Metrics_TotalLines = table.Column<int>(type: "INTEGER", nullable: true),
                    Metrics_CyclomaticComplexity = table.Column<int>(type: "INTEGER", nullable: true),
                    Metrics_TypeCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Metrics_MethodCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Metrics_NamespaceCount = table.Column<int>(type: "INTEGER", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileRecords_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    Recommendations = table.Column<string>(type: "TEXT", nullable: false),
                    HealthScore = table.Column<int>(type: "INTEGER", nullable: false),
                    HighSeverityCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MediumSeverityCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LowSeverityCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalFindingsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PdfReportPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    JsonReportPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PatchFilesPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ReportVersion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AnalysisDurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reports_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LineRange_Start = table.Column<int>(type: "INTEGER", nullable: true),
                    LineRange_End = table.Column<int>(type: "INTEGER", nullable: true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentType = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    SuggestedFix = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: true),
                    FixedCodeSnippet = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: true),
                    OriginalCodeSnippet = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewFindings_FileRecords_FileRecordId",
                        column: x => x.FileRecordId,
                        principalTable: "FileRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReviewFindings_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_IsActive",
                table: "ApiKeys",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyPrefix",
                table: "ApiKeys",
                column: "KeyPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_FileRecords_Extension",
                table: "FileRecords",
                column: "Extension");

            migrationBuilder.CreateIndex(
                name: "IX_FileRecords_ProjectId",
                table: "FileRecords",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ApiKeyId",
                table: "Projects",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CreatedAt",
                table: "Projects",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Status",
                table: "Projects",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ProjectId",
                table: "Reports",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_Category",
                table: "ReviewFindings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_FileRecordId",
                table: "ReviewFindings",
                column: "FileRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_ProjectId",
                table: "ReviewFindings",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_ReportId",
                table: "ReviewFindings",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_Severity",
                table: "ReviewFindings",
                column: "Severity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "ReviewFindings");

            migrationBuilder.DropTable(
                name: "FileRecords");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
