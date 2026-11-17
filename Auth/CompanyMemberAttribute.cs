using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using NodiaApi.Data;
using NodiaApi.Models;

namespace NodiaApi.Auth;

/// <summary>
/// Kräver att den inloggade användaren är medlem i företaget i route-värdet
/// <c>{companyId}</c>. Ska sitta på ALLA actions/controllers vars route innehåller
/// {companyId} – ett arkitekturtest kontrollerar det.
///
/// Attributet bär bara metadata (lägsta roll). Själva kontrollen görs av
/// <see cref="CompanyMemberFilter"/>, som MVC skapar per request via DI
/// (TypeFilterAttribute + ActivatorUtilities) med requestens scoped AppDbContext.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class CompanyMemberAttribute : TypeFilterAttribute
{
    public CompanyMemberAttribute(CompanyRole minimumRole = CompanyRole.Member)
        : base(typeof(CompanyMemberFilter))
    {
        MinimumRole = minimumRole;
        Arguments = [minimumRole];
        IsReusable = false; // ny filterinstans per request – delar aldrig en DbContext
    }

    public CompanyRole MinimumRole { get; }
}

/// <summary>
/// Medlemskapskontrollen bakom <see cref="CompanyMemberAttribute"/>.
/// - Inte medlem / okänt företag  -> 404 (avslöjar inte att företaget finns)
/// - Medlem men för låg roll       -> 403
/// - Annars körs action. Action ska ÄNDÅ filtrera sina frågor på companyId.
/// </summary>
public sealed class CompanyMemberFilter(AppDbContext db, CompanyRole minimumRole) : IAsyncActionFilter
{
    public const string RouteKey = "companyId";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Route-constrainten {companyId:guid} gör att ett ogiltigt id aldrig når hit,
        // men vi litar inte blint på det.
        if (!Guid.TryParse(context.RouteData.Values[RouteKey]?.ToString(), out var companyId))
        {
            context.Result = new NotFoundResult();
            return;
        }

        var userId = context.HttpContext.User.GetUserId();

        var role = await db.CompanyMemberships
            .Where(m => m.CompanyId == companyId && m.UserId == userId)
            .Select(m => (CompanyRole?)m.Role)
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (role is null)
        {
            context.Result = new NotFoundResult();
            return;
        }

        if (!HasAtLeast(role.Value, minimumRole))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }

        await next();
    }

    private static bool HasAtLeast(CompanyRole actual, CompanyRole required) =>
        required switch
        {
            CompanyRole.Member => actual is CompanyRole.Member or CompanyRole.Owner,
            CompanyRole.Owner => actual is CompanyRole.Owner,
            _ => false,
        };
}
