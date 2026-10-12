using AiOralExam.Modules.AccessConfig.Contracts;
using AiOralExam.Modules.AccessConfig.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiOralExam.Modules.AccessConfig.Infrastructure;

/// <summary>
/// DbContext of F7. NO EF migrations: tables are created by database/01_schema.sql.
/// Table/column names are snake_case (UseSnakeCaseNamingConvention in AccessConfigModule).
/// To change the schema: edit the SQL file first, then update the entities + mapping here to match.
/// </summary>
public sealed class AccessConfigDbContext(DbContextOptions<AccessConfigDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<EmailLinkToken> EmailLinkTokens => Set<EmailLinkToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AiServiceConfig> AiServiceConfigs => Set<AiServiceConfig>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseLecturer> CourseLecturers => Set<CourseLecturer>();
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Rubric> Rubrics => Set<Rubric>();
    public DbSet<RubricCriterion> RubricCriteria => Set<RubricCriterion>();
    public DbSet<Participant> Participants => Set<Participant>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums are stored as VARCHAR (matching the CHECK constraints)
        builder.Properties<TokenPurpose>().HaveConversion<string>();
        builder.Properties<AiServiceType>().HaveConversion<string>();
        builder.Properties<SpeechLanguage>().HaveConversion<string>();
        builder.Properties<ExamSessionStatus>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(20).IsRequired();
        });

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
            e.HasOne(x => x.Account).WithOne(x => x.User).HasForeignKey<UserAccount>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserAccount>(e =>
        {
            e.ToTable("user_accounts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.AccountName).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.PasswordSalt).HasMaxLength(128);
        });

        b.Entity<EmailLinkToken>(e =>
        {
            e.ToTable("email_link_tokens");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.UserAccount).WithMany().HasForeignKey(x => x.UserAccountId);
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.Id);
        });

        b.Entity<AiServiceConfig>(e =>
        {
            e.ToTable("ai_service_configs");
            e.HasKey(x => x.Id);
        });

        b.Entity<Course>(e =>
        {
            e.ToTable("courses");
            e.HasKey(x => x.Id);
        });

        b.Entity<CourseLecturer>(e =>
        {
            e.ToTable("course_lecturers");
            e.HasKey(x => new { x.CourseId, x.LecturerId });
            e.HasOne(x => x.Course).WithMany(c => c.Lecturers).HasForeignKey(x => x.CourseId);
            e.HasOne(x => x.Lecturer).WithMany(u => u.CourseAssignments).HasForeignKey(x => x.LecturerId);
        });

        b.Entity<ExamSession>(e =>
        {
            e.ToTable("exam_sessions");
            e.HasKey(x => x.Id);
            e.Ignore(x => x.IsLocked);
            e.HasOne(x => x.Course).WithMany(c => c.ExamSessions).HasForeignKey(x => x.CourseId);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(q => q.ExamSessionId);
        });

        b.Entity<Question>(e =>
        {
            e.ToTable("questions");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Rubric).WithOne().HasForeignKey<Rubric>(r => r.QuestionId);
        });

        b.Entity<Rubric>(e =>
        {
            e.ToTable("rubrics");
            e.HasKey(x => x.Id);
            e.Property(x => x.MaxScore).HasPrecision(5, 2).HasDefaultValue(0m).ValueGeneratedOnAddOrUpdate();
            e.HasMany(x => x.Criteria).WithOne(x => x.Rubric).HasForeignKey(x => x.RubricId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RubricCriterion>(e =>
        {
            e.ToTable("rubric_criteria");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrderNo).HasColumnName("order_no");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.MaxPoints).HasColumnName("max_points").HasPrecision(5, 2);
            e.HasIndex(x => new { x.RubricId, x.OrderNo }).IsUnique();
        });

        // Guid ids are generated in code (Guid.NewGuid). ValueGeneratedNever makes EF always INSERT new
        // entities attached to a navigation instead of mistaking them for existing rows to UPDATE.
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var key in entity.GetKeys())
                foreach (var prop in key.Properties.Where(p => p.ClrType == typeof(Guid)))
                    prop.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }
}
