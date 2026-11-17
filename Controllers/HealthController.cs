using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodiaApi.Data;

namespace NodiaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous] // Uttryckligt undantag från FallbackPolicy: hälsokontroll utan inloggning.
public class HealthController(AppDbContext db) : ControllerBase
{
    // GET /api/health/db – checks that the API can reach Supabase Postgres
    [HttpGet("db")]
    public async Task<IActionResult> Database()
    {
        try
        {
            var ok = await db.Database.CanConnectAsync();
            return ok ? Ok(new { database = "connected" })
                      : StatusCode(503, new { database = "unreachable" });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { database = "error", message = ex.Message });
        }
    }
}
