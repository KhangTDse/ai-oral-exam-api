using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using AiOralExam.Modules.AccessConfig.Application.Auth;
using AiOralExam.Modules.AccessConfig.Application.Courses;
using AiOralExam.Modules.AccessConfig.Domain;
using AiOralExam.Modules.AccessConfig.Infrastructure;
using AiOralExam.SharedKernel.Errors;
using AiOralExam.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ValidationException = AiOralExam.SharedKernel.Errors.ValidationException;

namespace AiOralExam.Modules.AccessConfig.Application.Users;
public sealed record UserDto(
    Guid Id,
    string? FullName,
    string? Email,
    string Role,
    string? StudentCode,
    string? LecturerCode,
    bool IsActive);

public sealed record CreateUserRequest(
    [Required] string FullName,
    [EmailAddress, Required] string Email,
    UserRole Role, string? StudentCode,
    string? LecturerCode);

public sealed record UpdateUserRequest(
    [Required] string FullName,
    string? StudentCode,
    string? LecturerCode,
    bool IsActive,
    UserRole? Role = null);

internal sealed record UserUpdateValues(
    string FullName,
    UserRole Role,
    string? StudentCode,
    string? LecturerCode,
    bool IsActive);

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(UserRole? role, bool? active, string? search, CancellationToken ct);
    Task<UserDto> GetAsync(Guid id, CancellationToken ct);
    Task<UserDto> CreateAsync(CreateUserRequest r, CancellationToken ct);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest r, CancellationToken ct);
}
internal sealed class UserService(
    AccessConfigDbContext db,
    ICurrentUser me,
    IAccountSetupEmailSender emailSender,
    TimeProvider timeProvider) : IUserService
{
    static string? Clean(string? x)
        => string.IsNullOrWhiteSpace(x) ? null : x.Trim();

    static void ValidateCodes(UserRole role, string? s, string? l)
    {
        if (!Enum.IsDefined(role))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["role"] = ["A valid user role is required."]
            });
        if (role == UserRole.Student && string.IsNullOrWhiteSpace(s))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                {
                    "studentCode", ["Student requires a student code."]
                }
            });
        if (role == UserRole.Lecturer && string.IsNullOrWhiteSpace(l))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                {
                    "lecturerCode", ["Lecturer requires a lecturer code."]
                }
            });
        if (role != UserRole.Student && s is not null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                {
                    "studentCode", ["Only students can have a student code."]
                }
            });
        if (role != UserRole.Lecturer && l is not null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                {
                    "lecturerCode", ["Only lecturers can have a lecturer code."]
                }
            });
    }

    internal static UserUpdateValues PrepareUpdate(User u, UpdateUserRequest r)
    {
        var currentRole = Enum.Parse<UserRole>(u.Role.Name);
        var role = r.Role ?? currentRole;
        var s = Clean(r.StudentCode);
        var l = Clean(r.LecturerCode);
        ValidateCodes(role, s, l);
        return new UserUpdateValues(r.FullName.Trim(), role, s, l, r.IsActive);
    }

    internal static void ApplyUpdate(User u, UserUpdateValues values, Role role)
    {
        u.FullName = values.FullName;
        u.Role = role;
        u.RoleId = role.Id;
        u.StudentCode = values.StudentCode;
        u.LecturerCode = values.LecturerCode;
        u.IsActive = values.IsActive;
    }

    static UserDto D(User u)
        => new(
        u.Id,
        u.FullName,
        u.Account?.Email,
        u.Role.Name,
        u.StudentCode,
        u.LecturerCode,
        u.IsActive);

    public async Task<IReadOnlyList<UserDto>> ListAsync(UserRole? role, bool? active, string? search, CancellationToken ct)
    {
        IQueryable<User> q = db.Users.AsNoTracking().Include(x => x.Role).Include(x => x.Account);
        if (role is not null)
            q = q.Where(x => x.Role.Name == role.ToString());
        if (active is not null)
            q = q.Where(x => x.IsActive == active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var p = $"%{search.Trim()}%";
            q = q.Where(x => (x.FullName != null && EF.Functions.ILike(x.FullName, p)) ||
            (x.Account != null && EF.Functions.ILike(x.Account.Email, p)) ||
            EF.Functions.ILike(x.StudentCode ?? "", p) ||
            EF.Functions.ILike(x.LecturerCode ?? "", p));
        }
        var rows = await q.OrderBy(x => x.FullName).ToListAsync(ct);
        return rows.Select(D).ToList();
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct)
        => D(await db.Users.Include(x => x.Role).Include(x => x.Account)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ??
            throw new NotFoundException("user_not_found", "User not found."));

    public async Task<UserDto> CreateAsync(CreateUserRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        var s = Clean(r.StudentCode);
        var l = Clean(r.LecturerCode);
        ValidateCodes(r.Role, s, l);
        if (await db.UserAccounts.AnyAsync(x => x.Email.ToLower() == email, ct))
            throw new ConflictException("email_exists", "Email is already in use.");
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Name == r.Role.ToString(), ct) ??
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["role"] = ["The requested role is not configured."]
            });
        if (s is not null && await db.Users.AnyAsync(x => x.StudentCode == s, ct))
            throw new ConflictException("student_code_exists", "Student code is already in use.");
        if (l is not null && await db.Users.AnyAsync(x => x.LecturerCode == l, ct))
            throw new ConflictException("lecturer_code_exists", "Lecturer code is already in use.");
        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            AccountName = null,
            Email = email,
            EmailVerified = false,
            PasswordSalt = null,
            GoogleLinked = false
        };

        var u = new User
        {
            Id = Guid.NewGuid(),
            FullName = r.FullName.Trim(),
            RoleId = role.Id,
            Role = role,
            StudentCode = s,
            LecturerCode = l,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Account = account
        };
        var (rawToken, tokenHash) = AccountSetupTokens.Create();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        db.EmailLinkTokens.Add(new EmailLinkToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = account.Id,
            UserAccount = account,
            TokenHash = tokenHash,
            Purpose = TokenPurpose.AccountSetup,
            ExpiresAt = now.AddDays(3),
            CreatedAt = now
        });
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Users.Add(u);
            await db.SaveChangesAsync(ct);
            await emailSender.SendAccountSetupAsync(email, rawToken, ct);
            await CourseService.Audit(db, me.Id, "Create", "User", u.Id, ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_user_accounts_email"
        })
        {
            throw new ConflictException("email_exists", "Email is already in use.");
        }
        return D(u);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest r, CancellationToken ct)
    {
        var u = await db.Users.Include(x => x.Role).Include(x => x.Account)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ??
            throw new NotFoundException("user_not_found", "User not found.");
        var values = PrepareUpdate(u, r);
        if (values.StudentCode is not null && await db.Users.AnyAsync(x => x.StudentCode == values.StudentCode && x.Id != id, ct))
            throw new ConflictException("student_code_exists", "Student code is already in use.");
        if (values.LecturerCode is not null && await db.Users.AnyAsync(x => x.LecturerCode == values.LecturerCode && x.Id != id, ct))
            throw new ConflictException("lecturer_code_exists", "Lecturer code is already in use.");
        var role = values.Role == Enum.Parse<UserRole>(u.Role.Name)
            ? u.Role
            : await db.Roles.FirstOrDefaultAsync(x => x.Name == values.Role.ToString(), ct)
                ?? throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["role"] = ["The requested role is not configured."]
                });
        ApplyUpdate(u, values, role);
        await db.SaveChangesAsync(ct);
        await CourseService.Audit(db, me.Id, "Update", "User", id, ct);
        return D(u);
    }

}
