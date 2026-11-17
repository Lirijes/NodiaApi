using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NodiaApi.Auth;
using NodiaApi.Data;
using NodiaApi.Models;

namespace NodiaApi.Controllers;

/// <summary>
/// Företag ("Workspaces" i frontend).
///
/// Säkerhetsregler:
/// - Användar-id hämtas ALLTID från den verifierade JWT:n (User.GetUserId()).
/// - Alla frågor utgår från användarens medlemskap – ett företag läses aldrig enbart på sitt id.
/// - Routes med {companyId} skyddas av [CompanyMember]; icke-medlemmar får 404.
/// </summary>
[ApiController]
[Route("api/companies")]
[Authorize]
public class CompaniesController(AppDbContext db) : ControllerBase
{
    public const int NameMaxLength = 100;

    // POST /api/companies – skapar ett företag; den inloggade användaren blir Owner.
    [HttpPost]
    public async Task<ActionResult<CompanyResponse>> Create(CreateCompanyRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > NameMaxLength)
        {
            ModelState.AddModelError(nameof(CreateCompanyRequest.Name),
                $"Name must be between 1 and {NameMaxLength} characters.");
            return ValidationProblem(ModelState);
        }

        var userId = User.GetUserId();

        // Ägare, användar-id och skapare sätts av servern – aldrig från klienten.
        var company = new Company { Name = name, CreatedBy = userId };
        company.Memberships.Add(new CompanyMembership { UserId = userId, Role = CompanyRole.Owner });
        db.Companies.Add(company);

        try
        {
            // Ett SaveChanges = en transaktion: företag och medlemskap skapas tillsammans eller inte alls.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsMissingAuthUser(ex))
        {
            // Token är fortfarande giltig men användaren har raderats i Supabase Auth.
            return Unauthorized();
        }

        var response = new CompanyResponse(company.Id, company.Name, CompanyRole.Owner, company.CreatedAt);
        return CreatedAtAction(nameof(Get), new { companyId = company.Id }, response);
    }

    // GET /api/companies – företag där den inloggade användaren är medlem.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyResponse>>> List(CancellationToken ct)
    {
        var userId = User.GetUserId();

        var companies = await db.CompanyMemberships
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Company.Name)
            .ThenBy(m => m.CompanyId)
            .Select(m => new CompanyResponse(m.Company.Id, m.Company.Name, m.Role, m.Company.CreatedAt))
            .ToListAsync(ct);

        return Ok(companies);
    }

    // GET /api/companies/{companyId} – ett företag; 404 om användaren inte är medlem.
    [HttpGet("{companyId:guid}")]
    [CompanyMember]
    public async Task<ActionResult<CompanyResponse>> Get(Guid companyId, CancellationToken ct)
    {
        var userId = User.GetUserId();

        // Försvar på djupet: frågan filtrerar på medlemskap även fast filtret redan kontrollerat det.
        var company = await db.CompanyMemberships
            .Where(m => m.CompanyId == companyId && m.UserId == userId)
            .Select(m => new CompanyResponse(m.Company.Id, m.Company.Name, m.Role, m.Company.CreatedAt))
            .FirstOrDefaultAsync(ct);

        return company is null ? NotFound() : Ok(company);
    }

    private static bool IsMissingAuthUser(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: "fk_company_memberships_auth_users_user_id" or "fk_companies_auth_users_created_by",
        };
}

/// <summary>Body för POST /api/companies. Innehåller medvetet BARA namnet.</summary>
public record CreateCompanyRequest([Required] string? Name);

public record CompanyResponse(Guid Id, string Name, CompanyRole Role, DateTimeOffset CreatedAt);
