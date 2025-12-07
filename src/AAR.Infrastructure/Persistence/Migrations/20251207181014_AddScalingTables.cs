using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AAR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScalingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    LastProcessedFileIndex = table.Column<int>(type: "int", nullable: false),
                    LastProcessedChunkOffset = table.Column<long>(type: "bigint", nullable: false),
                    TotalFilesCount = table.Column<int>(type: "int", nullable: false),
                    FilesProcessedCount = table.Column<int>(type: "int", nullable: false),
                    ChunksIndexedCount = table.Column<int>(type: "int", nullable: false),
                    EmbeddingsCreatedCount = table.Column<int>(type: "int", nullable: false),
                    ChunksSkippedCount = table.Column<int>(type: "int", nullable: false),
                    TotalTokensProcessed = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedTotalTokens = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    LastCheckpointAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessingStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingCompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SerializedState = table.Column<string>(type: "nvarchar(max)", maxLength: 50000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobCheckpoints_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationQuotas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TotalCredits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreditsUsed = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MaxConcurrentJobs = table.Column<int>(type: "int", nullable: false),
                    ActiveJobCount = table.Column<int>(type: "int", nullable: false),
                    MaxStorageBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageUsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    TokensConsumed = table.Column<long>(type: "bigint", nullable: false),
                    MaxTokensPerPeriod = table.Column<long>(type: "bigint", nullable: false),
                    PeriodStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsSuspended = table.Column<bool>(type: "bit", nullable: false),
                    SuspensionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationQuotas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UploadSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProjectDescription = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TotalSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalParts = table.Column<int>(type: "int", nullable: false),
                    UploadedParts = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    BytesUploaded = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobCheckpoints_LastCheckpointAt",
                table: "JobCheckpoints",
                column: "LastCheckpointAt");

            migrationBuilder.CreateIndex(
                name: "IX_JobCheckpoints_ProjectId",
                table: "JobCheckpoints",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCheckpoints_Status",
                table: "JobCheckpoints",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationQuotas_OrganizationId",
                table: "OrganizationQuotas",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationQuotas_PeriodEndDate",
                table: "OrganizationQuotas",
                column: "PeriodEndDate");

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_ApiKeyId",
                table: "UploadSessions",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_ExpiresAt",
                table: "UploadSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_Status",
                table: "UploadSessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobCheckpoints");

            migrationBuilder.DropTable(
                name: "OrganizationQuotas");

            migrationBuilder.DropTable(
                name: "UploadSessions");
        }
    }
}
