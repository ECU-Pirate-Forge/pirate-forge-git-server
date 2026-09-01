using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PirateForgeGitServer.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PirateForgeDbContext))]
[Migration("20260805135716_AddCourses")]
public sealed class AddCourses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Courses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(
                    type: "TEXT",
                    maxLength: 30,
                    nullable: false),
                Name = table.Column<string>(
                    type: "TEXT",
                    maxLength: 200,
                    nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(
                    type: "TEXT",
                    nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Courses", value => value.Id);
            });

        migrationBuilder.CreateTable(
            name: "CourseMemberships",
            columns: table => new
            {
                CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                Role = table.Column<string>(
                    type: "TEXT",
                    maxLength: 20,
                    nullable: false),
                AddedAt = table.Column<DateTimeOffset>(
                    type: "TEXT",
                    nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_CourseMemberships",
                    value => new { value.CourseId, value.UserId });
                table.ForeignKey(
                    name: "FK_CourseMemberships_AspNetUsers_UserId",
                    column: value => value.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CourseMemberships_Courses_CourseId",
                    column: value => value.CourseId,
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CourseMemberships_UserId",
            table: "CourseMemberships",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Courses_Code",
            table: "Courses",
            column: "Code",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CourseMemberships");
        migrationBuilder.DropTable(name: "Courses");
    }
}
