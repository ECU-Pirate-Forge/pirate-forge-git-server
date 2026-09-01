using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PirateForgeGitServer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseAuditEvents_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseAuditEvents_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CourseAuditEvents_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseAuditEvents_GitRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "GitRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseAuditEvents_ActorUserId",
                table: "CourseAuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseAuditEvents_AssignmentId",
                table: "CourseAuditEvents",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseAuditEvents_CourseId_CreatedAt",
                table: "CourseAuditEvents",
                columns: ["CourseId", "CreatedAt"]);

            migrationBuilder.CreateIndex(
                name: "IX_CourseAuditEvents_RepositoryId",
                table: "CourseAuditEvents",
                column: "RepositoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseAuditEvents");
        }
    }
}
