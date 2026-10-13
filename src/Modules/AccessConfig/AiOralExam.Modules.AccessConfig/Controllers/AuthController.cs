using AiOralExam.Modules.AccessConfig.Application.Auth;
using AiOralExam.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AiOralExam.Modules.AccessConfig.Controllers;

/// <summary>Sign-in happens in Keycloak (realm aives); clients send its access token as a Bearer token.</summary>
[ApiController]
[Route("api/auth")]
[Tags("Auth")]
public sealed class AuthController(IAuthService auth, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Profile of the signed-in account.</summary>
    [HttpGet("me")]
    [Authorize]
    public Task<UserProfileDto> Me(CancellationToken ct) =>
        auth.GetProfileAsync(currentUser.Id, ct);
}
