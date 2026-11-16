using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NodiaApi.Controllers;

/// <summary>
/// Information om den inloggade användaren.
/// Allt hämtas från den VERIFIERADE access token (JWT) – aldrig från
/// id:n eller värden som frontend skickar i URL eller body.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    // GET /api/me
    [HttpGet]
    public ActionResult<MeResponse> Get()
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            // Ska inte kunna hända med en giltig Supabase-token, men vi litar inte blint.
            return Unauthorized();
        }

        return Ok(new MeResponse(
            Id: userId,
            Email: User.FindFirst("email")?.Value,
            Role: User.FindFirst("role")?.Value,
            SessionId: User.FindFirst("session_id")?.Value));
    }
}

public record MeResponse(string Id, string? Email, string? Role, string? SessionId);
