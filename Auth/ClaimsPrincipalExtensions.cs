using System.Security.Claims;

namespace NodiaApi.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Den inloggade användarens Supabase-id (auth.users.id), hämtat ENBART från
    /// "sub" i den verifierade JWT:n. Tar medvetet inga parametrar – ett id från
    /// URL, body eller headers kan aldrig användas som identitet.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Om "sub" saknas eller inte är ett uuid. Auktoriseringspolicyn i Program.cs
    /// stoppar sådana tokens innan de når en controller, så detta är ett programmeringsfel.
    /// </exception>
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated user has no valid 'sub' claim.");
}
