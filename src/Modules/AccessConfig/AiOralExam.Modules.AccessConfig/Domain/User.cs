namespace AiOralExam.Modules.AccessConfig.Domain;

/// <summary>ERD: USER + ROLE. Class diagram: User (abstract) + Student/Lecturer/Administrator stored in one table.</summary>
public class User
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = null!;
    public short RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public UserAccount? Account { get; set; }
    public string? StudentCode { get; set; }
    public string? LecturerCode { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<CourseLecturer> CourseAssignments { get; set; } = new List<CourseLecturer>();
}

public class Role
{
    public short Id { get; set; }
    public string Name { get; set; } = null!;
}

public class UserAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string? AccountName { get; set; }
    public string Email { get; set; } = null!;
    public bool EmailVerified { get; set; }
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    public bool GoogleLinked { get; set; }
}

/// <summary>ERD: EMAIL LINK TOKEN. Only the SHA-256 of the token is stored, never the raw token.</summary>
public class EmailLinkToken
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public UserAccount UserAccount { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public TokenPurpose Purpose { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsUsable(DateTime utcNow) => UsedAt is null && ExpiresAt > utcNow;
}

/// <summary>ERD: AUDIT LOG.</summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Action { get; set; } = null!;
    public string TargetEntity { get; set; } = null!;
    public Guid? TargetId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>ERD: AI SERVICE CONFIG. Only one IsActive row per ServiceType.</summary>
public class AiServiceConfig
{
    public Guid Id { get; set; }
    public Guid ConfiguredBy { get; set; }
    public AiServiceType ServiceType { get; set; }
    public string ProviderName { get; set; } = null!;
    public string EndpointUrl { get; set; } = null!;
    public string ApiKeyEncrypted { get; set; } = null!;
    public SpeechLanguage Language { get; set; } = SpeechLanguage.Vietnamese;
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}
