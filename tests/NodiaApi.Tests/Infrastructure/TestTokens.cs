using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NodiaApi.Tests.Infrastructure;

/// <summary>
/// Skapar JWT:er som ser ut som Supabase access tokens (ES256), signerade med en
/// testnyckel. API:t validerar dem med EXAKT samma TokenValidationParameters som i
/// produktion – det enda som skiljer är varifrån signeringsnyckeln hämtas
/// (se <see cref="NodiaApiFactory"/>).
/// </summary>
public static class TestTokens
{
    /// <summary>Samma issuer som produktion: {Supabase:Url}/auth/v1 från appsettings.json.</summary>
    public const string Issuer = "https://xrcmaxgpqcqnimeeomuk.supabase.co/auth/v1";

    public static readonly ECDsaSecurityKey SigningKey = NewKey("test-signing-key");

    public static ECDsaSecurityKey NewKey(string keyId) =>
        new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = keyId };

    /// <summary>En giltig token för en vanlig inloggad Supabase-användare.</summary>
    public static string ForUser(Guid userId) => Create(UserClaims(userId));

    public static Dictionary<string, object> UserClaims(Guid userId) => new()
    {
        ["sub"] = userId.ToString(),
        ["role"] = "authenticated",
        ["email"] = $"{userId:N}@example.test",
        ["is_anonymous"] = false,
    };

    public static string Create(
        IDictionary<string, object> claims,
        string audience = "authenticated",
        string issuer = Issuer,
        DateTime? expiresUtc = null,
        SecurityKey? signingKey = null,
        bool unsigned = false)
    {
        var expires = expiresUtc ?? DateTime.UtcNow.AddHours(1);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = expires.AddHours(-1),
            NotBefore = expires.AddHours(-1),
            Expires = expires,
            SigningCredentials = unsigned
                ? null
                : new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.EcdsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
