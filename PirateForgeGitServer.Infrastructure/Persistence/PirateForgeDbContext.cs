using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Domain.Git;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Infrastructure.Identity;

namespace PirateForgeGitServer.Infrastructure.Persistence;

public sealed class PirateForgeDbContext(DbContextOptions<PirateForgeDbContext> options)
    : IdentityDbContext<PirateForgeUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();

    public DbSet<GitRepository> GitRepositories => Set<GitRepository>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<CourseMembership> CourseMemberships => Set<CourseMembership>();

    public DbSet<Assignment> Assignments => Set<Assignment>();

    public DbSet<AssignmentTeam> AssignmentTeams => Set<AssignmentTeam>();

    public DbSet<AssignmentTeamMember> AssignmentTeamMembers =>
        Set<AssignmentTeamMember>();

    public DbSet<AssignmentTeamContribution> AssignmentTeamContributions =>
        Set<AssignmentTeamContribution>();

    public DbSet<AssignmentRepository> AssignmentRepositories =>
        Set<AssignmentRepository>();

    public DbSet<AssignmentSubmissionRun> AssignmentSubmissionRuns =>
        Set<AssignmentSubmissionRun>();

    public DbSet<AssignmentReviewQuestion> AssignmentReviewQuestions =>
        Set<AssignmentReviewQuestion>();

    public DbSet<CourseAuditEvent> CourseAuditEvents => Set<CourseAuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PersonalAccessToken>(token =>
        {
            token.ToTable("PersonalAccessTokens");
            token.HasKey(value => value.Id);
            token.Property(value => value.Name).HasMaxLength(100);
            token.Property(value => value.Prefix).HasMaxLength(12);
            token.Property(value => value.SecretHash).HasMaxLength(32);
            token.HasIndex(value => value.Prefix).IsUnique();
            token.HasIndex(value => value.UserId);
            token.HasOne<PirateForgeUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GitRepository>(repository =>
        {
            repository.ToTable("GitRepositories");
            repository.HasKey(value => value.Id);
            repository.Property(value => value.Name).HasMaxLength(100);
            repository.Property(value => value.DisplayName).HasMaxLength(200);
            repository.Property(value => value.Purpose)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(RepositoryPurpose.Standard);
            repository.HasIndex(value => value.Name).IsUnique();
            repository.HasIndex(value => value.OwnerUserId);
            repository.HasOne<PirateForgeUser>()
                .WithMany()
                .HasForeignKey(value => value.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Course>(course =>
        {
            course.ToTable("Courses");
            course.HasKey(value => value.Id);
            course.Property(value => value.Code).HasMaxLength(30);
            course.Property(value => value.Name).HasMaxLength(200);
            course.HasIndex(value => value.Code).IsUnique();
            course.HasMany(value => value.Memberships)
                .WithOne()
                .HasForeignKey(value => value.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
            course.Navigation(value => value.Memberships)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<CourseMembership>(membership =>
        {
            membership.ToTable("CourseMemberships");
            membership.HasKey(value => new { value.CourseId, value.UserId });
            membership.Property(value => value.Role).HasMaxLength(20);
            membership.HasIndex(value => value.UserId);
            membership.HasOne<PirateForgeUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Assignment>(assignment =>
        {
            assignment.ToTable("Assignments");
            assignment.HasKey(value => value.Id);
            assignment.Property(value => value.Slug).HasMaxLength(50);
            assignment.Property(value => value.Title).HasMaxLength(200);
            assignment.Property(value => value.Description).HasMaxLength(4000);
            assignment.HasIndex(value => new { value.CourseId, value.Slug })
                .IsUnique();
            assignment.HasOne<Course>()
                .WithMany()
                .HasForeignKey(value => value.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
            assignment.HasOne<GitRepository>()
                .WithMany()
                .HasForeignKey(value => value.TemplateRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
            assignment.HasOne<PirateForgeUser>()
                .WithMany()
                .HasForeignKey(value => value.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AssignmentTeam>(team =>
        {
            team.ToTable("AssignmentTeams");
            team.HasKey(value => value.Id);
            team.HasIndex(value => new { value.AssignmentId, value.Name })
                .IsUnique();
            team.HasIndex(value => value.RepositoryId).IsUnique();
            team.HasOne<Assignment>()
                .WithMany()
                .HasForeignKey(value => value.AssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
            team.HasOne<GitRepository>()
                .WithMany()
                .HasForeignKey(value => value.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AssignmentTeamMember>(teamMember =>
        {
            teamMember.ToTable("AssignmentTeamMembers");
            teamMember.HasKey(value => new { value.TeamId, value.UserId });
            teamMember.HasIndex(value => value.UserId);
            teamMember.HasOne<AssignmentTeam>()
                .WithMany()
                .HasForeignKey(value => value.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            teamMember.HasOne<PirateForgeUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AssignmentTeamContribution>(contribution =>
        {
            contribution.ToTable("AssignmentTeamContributions");
            contribution.HasKey(value => value.Id);
            contribution.HasIndex(value => new { value.TeamId, value.UserId })
                .IsUnique();
            contribution.HasOne<AssignmentTeam>()
                .WithMany()
                .HasForeignKey(value => value.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            contribution.HasOne<PirateForgeUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AssignmentRepository>(assignmentRepository =>
        {
            assignmentRepository.ToTable("AssignmentRepositories");
            assignmentRepository.HasKey(value => value.Id);
            assignmentRepository.HasIndex(value =>
                    new { value.AssignmentId, value.StudentUserId })
                .IsUnique();
            assignmentRepository.HasIndex(value => value.RepositoryId)
                .IsUnique();
            assignmentRepository.HasOne<Assignment>()
                .WithMany()
                .HasForeignKey(value => value.AssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
            assignmentRepository.HasOne<PirateForgeUser>()
                .WithMany()
                .HasForeignKey(value => value.StudentUserId)
                .OnDelete(DeleteBehavior.Restrict);
            assignmentRepository.HasOne<GitRepository>()
                .WithMany()
                .HasForeignKey(value => value.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AssignmentSubmissionRun>(submissionRun =>
        {
            submissionRun.ToTable("AssignmentSubmissionRuns");
            submissionRun.HasKey(value => value.Id);
            submissionRun.Property(value => value.CommitSha).HasMaxLength(64);
            submissionRun.Property(value => value.Status).HasMaxLength(20);
            submissionRun.Property(value => value.Summary).HasMaxLength(2000);
            submissionRun.Property(value => value.Error).HasMaxLength(2000);
            submissionRun.Property(value => value.AssessmentLog).HasMaxLength(8000);
            submissionRun.Property(value => value.ReviewSummary).HasMaxLength(2000);
            submissionRun.HasIndex(value => new
                {
                    value.AssignmentId,
                    value.StudentUserId,
                    value.CreatedAt,
                });
            submissionRun.HasOne<Assignment>()
                .WithMany()
                .HasForeignKey(value => value.AssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
            submissionRun.HasOne<GitRepository>()
                .WithMany()
                .HasForeignKey(value => value.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
            submissionRun.HasOne<PirateForgeUser>()
                .WithMany()
                .HasForeignKey(value => value.StudentUserId)
                .OnDelete(DeleteBehavior.Restrict);
            submissionRun.HasMany(value => value.Questions)
                .WithOne()
                .HasForeignKey(value => value.SubmissionRunId)
                .OnDelete(DeleteBehavior.Cascade);
            submissionRun.Navigation(value => value.Questions)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<AssignmentReviewQuestion>(question =>
        {
            question.ToTable("AssignmentReviewQuestions");
            question.HasKey(value => value.Id);
            question.Property(value => value.Question).HasMaxLength(2000);
            question.Property(value => value.FocusArea).HasMaxLength(200);
            question.Property(value => value.StudentAnswer).HasMaxLength(4000);
            question.HasIndex(value => value.SubmissionRunId);
            question.HasOne(value => value.SubmissionRun)
                .WithMany(value => value.Questions)
                .HasForeignKey(value => value.SubmissionRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CourseAuditEvent>(auditEvent =>
        {
            auditEvent.ToTable("CourseAuditEvents");
            auditEvent.HasKey(value => value.Id);
            auditEvent.Property(value => value.Action).HasMaxLength(100);
            auditEvent.HasIndex(value => new
            {
                value.CourseId,
                value.CreatedAt,
            });
            auditEvent.HasOne<Course>()
                .WithMany()
                .HasForeignKey(value => value.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
            auditEvent.HasOne<Assignment>()
                .WithMany()
                .HasForeignKey(value => value.AssignmentId)
                .OnDelete(DeleteBehavior.SetNull);
            auditEvent.HasOne<GitRepository>()
                .WithMany()
                .HasForeignKey(value => value.RepositoryId)
                .OnDelete(DeleteBehavior.SetNull);
            auditEvent.HasOne<PirateForgeUser>()
                .WithMany()
                .HasForeignKey(value => value.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
