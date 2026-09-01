using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PirateForgeGitServer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentSubmissionRunsAndReviewQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AskUnderstandingQuestions",
                table: "Assignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoGradeEnabled",
                table: "Assignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GradingCommand",
                table: "Assignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalAiModel",
                table: "Assignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxScore",
                table: "Assignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewPrompt",
                table: "Assignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssignmentSubmissionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommitSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    AssessmentLog = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ReviewSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentSubmissionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentSubmissionRuns_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentSubmissionRuns_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignmentSubmissionRuns_GitRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "GitRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssignmentReviewQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FocusArea = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StudentAnswer = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentReviewQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentReviewQuestions_AssignmentSubmissionRuns_SubmissionRunId",
                        column: x => x.SubmissionRunId,
                        principalTable: "AssignmentSubmissionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentReviewQuestions_SubmissionRunId",
                table: "AssignmentReviewQuestions",
                column: "SubmissionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentSubmissionRuns_AssignmentId_StudentUserId_CreatedAt",
                table: "AssignmentSubmissionRuns",
                columns: new[] { "AssignmentId", "StudentUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentSubmissionRuns_RepositoryId",
                table: "AssignmentSubmissionRuns",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentSubmissionRuns_StudentUserId",
                table: "AssignmentSubmissionRuns",
                column: "StudentUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentReviewQuestions");

            migrationBuilder.DropTable(
                name: "AssignmentSubmissionRuns");

            migrationBuilder.DropColumn(
                name: "AskUnderstandingQuestions",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "AutoGradeEnabled",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "GradingCommand",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "LocalAiModel",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "MaxScore",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "ReviewPrompt",
                table: "Assignments");
        }
    }
}
