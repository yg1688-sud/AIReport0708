using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIExport.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Dimensions = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Metrics = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ChartTypes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Filters = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ColumnNames = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Strategy = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalysisTemplates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UploadBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TotalFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    ReadyFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    BatchStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Strategy = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadBatches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SessionStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalysisSessions_AnalysisTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "AnalysisTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AnalysisSessions_UploadBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "UploadBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UploadedFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StoredPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    FileFormat = table.Column<int>(type: "INTEGER", nullable: false),
                    RowCount = table.Column<int>(type: "INTEGER", nullable: true),
                    ColumnCount = table.Column<int>(type: "INTEGER", nullable: true),
                    ColumnHeaders = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ParseStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadedFiles_UploadBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "UploadBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ReportStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportType = table.Column<int>(type: "INTEGER", nullable: false),
                    Chapters = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    PdfPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalysisReports_AnalysisSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AnalysisSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dimensions = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Metrics = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ChartTypes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Filters = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalysisRequirements_AnalysisSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AnalysisSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sender = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_AnalysisSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AnalysisSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisReports_CreatedAt",
                table: "AnalysisReports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisReports_ExpiresAt",
                table: "AnalysisReports",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisReports_SessionId",
                table: "AnalysisReports",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRequirements_SessionId",
                table: "AnalysisRequirements",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisSessions_BatchId",
                table: "AnalysisSessions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisSessions_TemplateId",
                table: "AnalysisSessions",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisTemplates_UserId_Name",
                table: "AnalysisTemplates",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SessionId_Timestamp",
                table: "ChatMessages",
                columns: new[] { "SessionId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatches_UserId",
                table: "UploadBatches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_BatchId",
                table: "UploadedFiles",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisReports");

            migrationBuilder.DropTable(
                name: "AnalysisRequirements");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "UploadedFiles");

            migrationBuilder.DropTable(
                name: "AnalysisSessions");

            migrationBuilder.DropTable(
                name: "AnalysisTemplates");

            migrationBuilder.DropTable(
                name: "UploadBatches");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
