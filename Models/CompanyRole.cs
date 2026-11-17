namespace NodiaApi.Models;

/// <summary>
/// Roll inom ett företag. Lagras som gemener ("owner", "member") i databasen,
/// där en CHECK-constraint stoppar alla andra värden.
/// </summary>
public enum CompanyRole
{
    Owner,
    Member,
}
