namespace NodiaApi.Models;

/// <summary>
/// Kopplar en Supabase Auth-användare till ett företag med en roll.
/// Detta är den ENDA källan till behörighet för företagsdata.
/// </summary>
public class CompanyMembership
{
    public Guid CompanyId { get; set; }

    /// <summary>
    /// Supabase Auth-användarens id (auth.users.id) = "sub" i den verifierade JWT:n.
    /// Sätts alltid av servern från token – aldrig från ett värde som klienten skickar.
    /// </summary>
    public Guid UserId { get; set; }

    public CompanyRole Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Company Company { get; set; } = null!;
}
