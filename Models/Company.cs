namespace NodiaApi.Models;

/// <summary>
/// Ett företag (i frontend: "Workspace"). All företagsdata hänger på ett Company
/// och får bara läsas av användare med ett <see cref="CompanyMembership"/>.
/// </summary>
public class Company
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Supabase Auth-användaren (auth.users.id) som skapade företaget.
    /// Endast historik – ger INGEN behörighet. Behörighet styrs enbart av Memberships.
    /// Blir null om användaren raderas.
    /// </summary>
    public Guid? CreatedBy { get; set; }

    public List<CompanyMembership> Memberships { get; } = [];
}
