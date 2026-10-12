using AiOralExam.Modules.AccessConfig.Application.Auth;
using AiOralExam.SharedKernel.Errors;
using AiOralExam.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AiOralExam.Modules.AccessConfig.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("Auth")]
public sealed class AuthController(IAuthService auth, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Sign in with email + password; returns a JWT that carries the role.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) =>
        auth.LoginWithPasswordAsync(request, ct);

    /// <summary>Set the account name and password using a single-use account setup link.</summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CompleteSetup(CompleteAccountSetupRequest request, CancellationToken ct)
    {
        await auth.CompleteAccountSetupAsync(request, ct);
        return NoContent();
    }

    /// <summary>Profile of the signed-in account.</summary>
    [HttpGet("me")]
    [Authorize]
    public Task<UserProfileDto> Me(CancellationToken ct) =>
        auth.GetProfileAsync(currentUser.Id, ct);

    // TODO (M4): POST google, POST forgot-password, POST reset-password
}
