using System.Security.Cryptography;
using System.Text;
using AiOralExam.SharedKernel.Errors;

namespace AiOralExam.Modules.AccessConfig.Application.Auth;

internal static class AccountSetupTokens
{
    internal static (string RawToken, string TokenHash) Create()
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return (rawToken, Hash(rawToken));
    }

    internal static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}

public interface IAccountSetupEmailSender
{
    Task SendAccountSetupAsync(string email, string rawToken, CancellationToken ct);
}

internal sealed class UnconfiguredAccountSetupEmailSender : IAccountSetupEmailSender
{
    public Task SendAccountSetupAsync(string email, string rawToken, CancellationToken ct) =>
        throw new AppException(503, "account_setup_email_unavailable",
            "Account setup email delivery is not configured.");
}
