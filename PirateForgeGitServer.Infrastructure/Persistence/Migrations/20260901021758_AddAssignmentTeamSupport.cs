using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PirateForgeGitServer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentTeamSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchiveFilePath",
                table: "AssignmentSubmissionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "AssignmentSubmissionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PurgedAt",
                table: "AssignmentSubmissionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowTeamChanges",
                table: "Assignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsGroupAssignment",
                table: "Assignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxTeamSize",
                table: "Assignments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinTeamSize",
                table: "Assignments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireTeamSelection",
                table: "Assignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AssignmentTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentTeams_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignmentTeams_GitRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "GitRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssignmentTeamContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BranchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CommitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentationCommitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitTestCommitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImplementationCommitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastContributionAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentTeamContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentTeamContributions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentTeamContributions_AssignmentTeams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "AssignmentTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssignmentTeamMembers",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentTeamMembers", x => new { x.TeamId, x.UserId });
                    table.ForeignKey(
                        name: "FK_AssignmentTeamMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssignmentTeamMembers_AssignmentTeams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "AssignmentTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTeamContributions_TeamId_UserId",
                table: "AssignmentTeamContributions",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTeamContributions_UserId",
                table: "AssignmentTeamContributions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTeamMembers_UserId",
                table: "AssignmentTeamMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTeams_AssignmentId_Name",
                table: "AssignmentTeams",
                columns: new[] { "AssignmentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentTeams_RepositoryId",
                table: "AssignmentTeams",
                column: "RepositoryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentTeamContributions");

            migrationBuilder.DropTable(
                name: "AssignmentTeamMembers");

            migrationBuilder.DropTable(
                name: "AssignmentTeams");

            migrationBuilder.DropColumn(
                name: "ArchiveFilePath",
                table: "AssignmentSubmissionRuns");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "AssignmentSubmissionRuns");

            migrationBuilder.DropColumn(
                name: "PurgedAt",
                table: "AssignmentSubmissionRuns");

            migrationBuilder.DropColumn(
                name: "AllowTeamChanges",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "IsGroupAssignment",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "MaxTeamSize",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "MinTeamSize",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "RequireTeamSelection",
                table: "Assignments");
        }
    }
}
