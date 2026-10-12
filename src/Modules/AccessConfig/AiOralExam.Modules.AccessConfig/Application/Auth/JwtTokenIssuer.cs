using System.Security.Claims;
using System.Text;
using AiOralExam.Modules.AccessConfig.Domain;
using AiOralExam.SharedKernel.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AiOralExam.Modules.AccessConfig.Application.Auth;

public interface IJwtTokenIssuer
{
    (string Token, DateTime ExpiresAt) Issue(UserAccount account);
}

internal sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : IJwtTokenIssuer
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAt) Issue(UserAccount account)
    {
        var user = account.User;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.ExpiresMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(AppClaims.UserId, user.Id.ToString()),
                new Claim(AppClaims.Email, account.Email),
                new Claim(AppClaims.Name, user.FullName ?? account.AccountName ?? account.Email),
                new Claim(AppClaims.Role, user.Role.Name),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
