using AiOralExam.Modules.AccessConfig.Application.Auth;
using AiOralExam.Modules.AccessConfig.Application.Users;
using AiOralExam.Modules.AccessConfig.Domain;
using AiOralExam.SharedKernel.Errors;

namespace AiOralExam.UnitTests;

public class AccountSetupTokenTests
{
    [Fact]
    public void Generated_token_is_unpredictable_and_only_its_hash_is_persistable()
    {
        var first = AccountSetupTokens.Create();
        var second = AccountSetupTokens.Create();

        Assert.NotEqual(first.RawToken, second.RawToken);
        Assert.Equal(AccountSetupTokens.Hash(first.RawToken), first.TokenHash);
        Assert.Equal(64, first.TokenHash.Length);
        Assert.DoesNotContain(first.RawToken, first.TokenHash);
    }

    [Fact]
    public void Email_link_is_usable_only_before_expiry_and_before_consumption()
    {
        var now = DateTime.UtcNow;
        var link = new EmailLinkToken
        {
            Purpose = TokenPurpose.AccountSetup,
            TokenHash = AccountSetupTokens.Hash("one-time-token"),
            ExpiresAt = now.AddMinutes(1)
        };

        Assert.True(link.IsUsable(now));
        Assert.False(link.IsUsable(now.AddMinutes(1)));

        link.UsedAt = now;
        Assert.False(link.IsUsable(now));
    }

    [Fact]
    public void Administrator_create_request_has_no_password_field()
    {
        Assert.Null(typeof(CreateUserRequest).GetProperty("Password"));
        Assert.Equal(5, typeof(CreateUserRequest).GetConstructors().Single().GetParameters().Length);
    }

    [Fact]
    public async Task Unconfigured_email_boundary_fails_without_exposing_the_token()
    {
        const string rawToken = "must-not-appear-in-errors-or-logs";
        var sender = new UnconfiguredAccountSetupEmailSender();

        var error = await Assert.ThrowsAsync<AppException>(() =>
            sender.SendAccountSetupAsync("user@example.test", rawToken, CancellationToken.None));

        Assert.Equal(503, error.StatusCode);
        Assert.DoesNotContain(rawToken, error.Message);
    }
}
