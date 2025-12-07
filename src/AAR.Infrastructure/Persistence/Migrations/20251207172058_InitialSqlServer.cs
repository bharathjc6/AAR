using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AAR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    KeyPrefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestCount = table.Column<long>(type: "bigint", nullable: false),
                    Scopes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    GitRepoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AnalysisStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnalysisCompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    TotalLinesOfCode = table.Column<int>(type: "int", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Chunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChunkHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    StartLine = table.Column<int>(type: "int", nullable: false),
                    EndLine = table.Column<int>(type: "int", nullable: false),
                    TokenCount = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TextHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SemanticType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SemanticName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", maxLength: 100000, nullable: true),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", maxLength: 50000, nullable: true),
                    EmbeddingModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EmbeddingGeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chunks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FileRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    IsAnalyzed = table.Column<bool>(type: "bit", nullable: false),
                    Metrics_LinesOfCode = table.Column<int>(type: "int", nullable: true),
                    Metrics_TotalLines = table.Column<int>(type: "int", nullable: true),
                    Metrics_CyclomaticComplexity = table.Column<int>(type: "int", nullable: true),
                    Metrics_TypeCount = table.Column<int>(type: "int", nullable: true),
                    Metrics_MethodCount = table.Column<int>(type: "int", nullable: true),
                    Metrics_NamespaceCount = table.Column<int>(type: "int", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    Recommendations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HealthScore = table.Column<int>(type: "int", nullable: false),
                    HighSeverityCount = table.Column<int>(type: "int", nullable: false),
                    MediumSeverityCount = table.Column<int>(type: "int", nullable: false),
                    LowSeverityCount = table.Column<int>(type: "int", nullable: false),
                    TotalFindingsCount = table.Column<int>(type: "int", nullable: false),
                    PdfReportPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    JsonReportPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PatchFilesPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReportVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AnalysisDurationSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LineRange_Start = table.Column<int>(type: "int", nullable: true),
                    LineRange_End = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    AgentType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    SuggestedFix = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: true),
                    FixedCodeSnippet = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    OriginalCodeSnippet = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                        onDelete: ReferentialAction.Restrict);
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
                name: "IX_Chunks_ChunkHash",
                table: "Chunks",
                column: "ChunkHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chunks_ProjectId",
                table: "Chunks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Chunks_ProjectId_FilePath",
                table: "Chunks",
                columns: new[] { "ProjectId", "FilePath" });

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
                name: "Chunks");

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
