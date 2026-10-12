using AiOralExam.Modules.AccessConfig.Domain;
using AiOralExam.Modules.AccessConfig.Infrastructure;
using AiOralExam.SharedKernel.Errors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AiOralExam.Modules.AccessConfig.Application.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginWithPasswordAsync(LoginRequest request, CancellationToken ct = default);
    Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task CompleteAccountSetupAsync(CompleteAccountSetupRequest request, CancellationToken ct = default);

    // TODO (M4): LoginWithGoogleAsync(idToken), RequestPasswordResetAsync(email), SetPasswordAsync(token, password)
}

internal sealed class AuthService(
    AccessConfigDbContext db,
    IPasswordHasher<UserAccount> passwordHasher,
    IJwtTokenIssuer tokenIssuer,
    ILogger<AuthService> logger,
    TimeProvider timeProvider) : IAuthService
{
    // Same message for every failure -> does not reveal which emails have an account (R-08)
    private static UnauthorizedException InvalidCredentials() =>
        new("invalid_credentials", "Incorrect email or password.");

    public async Task<LoginResponse> LoginWithPasswordAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var account = await db.UserAccounts
            .Include(x => x.User).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Email.ToLower() == email, ct);

        if (account is null || !account.User.IsActive || account.PasswordHash is null)
        {
            logger.LogInformation("Login failed for {Email}", email);
            throw InvalidCredentials();
        }

        var result = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            logger.LogInformation("Login failed for user {UserId}", account.UserId);
            throw InvalidCredentials();
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = passwordHasher.HashPassword(account, request.Password);
            await db.SaveChangesAsync(ct);
        }

        var (token, expiresAt) = tokenIssuer.Issue(account);
        logger.LogInformation("User {UserId} logged in as {Role}", account.UserId, account.User.Role.Name);
        return new LoginResponse(token, expiresAt, ToDto(account.User, account));
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking()
                       .Include(x => x.Role).Include(x => x.Account)
                       .FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException("user_not_found", "Account not found.");
        return ToDto(user, user.Account);
    }

    public async Task CompleteAccountSetupAsync(CompleteAccountSetupRequest request, CancellationToken ct = default)
    {
        var tokenHash = AccountSetupTokens.Hash(request.Token);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var link = await db.EmailLinkTokens
            .Include(x => x.UserAccount)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.Purpose == TokenPurpose.AccountSetup, ct);

        if (link is null || !link.IsUsable(now))
            throw new UnauthorizedException("invalid_setup_token", "The account setup link is invalid or expired.");

        var consumed = await db.EmailLinkTokens
            .Where(x => x.Id == link.Id && x.Purpose == TokenPurpose.AccountSetup && x.UsedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAt, now), ct);
        if (consumed != 1)
            throw new UnauthorizedException("invalid_setup_token", "The account setup link is invalid or expired.");

        link.UserAccount.AccountName = request.AccountName.Trim();
        link.UserAccount.PasswordHash = passwordHasher.HashPassword(link.UserAccount, request.Password);
        link.UserAccount.EmailVerified = true;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_user_accounts_account_name"
        })
        {
            throw new ConflictException("account_name_exists", "Account name is already in use.");
        }

        await transaction.CommitAsync(ct);
    }

    private static UserProfileDto ToDto(User u, UserAccount? account) =>
        new(u.Id, u.FullName, account?.Email, u.Role.Name, u.StudentCode, u.LecturerCode);
}
