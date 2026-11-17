using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NodiaApi.Tests.Infrastructure;

namespace NodiaApi.Tests;

/// <summary>Oautentiserade och ogiltiga anrop nekas – på ALLA endpoints.</summary>
[Collection(ApiCollection.Name)]
public partial class AuthenticationTests(NodiaApiFactory factory)
{
    [Fact]
    public async Task Every_endpoint_except_explicit_AllowAnonymous_returns_401_without_token()
    {
        var client = factory.CreateClientWithToken(null);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .ToList();

        // Skyddar mot att testet "går igenom" för att inga endpoints hittades.
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "api/companies/{companyId:guid}");
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "api/companies");
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "api/me");

        foreach (var endpoint in endpoints)
        {
            var path = "/" + RouteParameter().Replace(endpoint.RoutePattern.RawText!, Guid.NewGuid().ToString());
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            foreach (var method in methods)
            {
                var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (method is "POST" or "PUT" or "PATCH")
                    request.Content = JsonContent.Create(new { name = "x" });

                var response = await client.SendAsync(request);

                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized,
                    $"{method} {path} returned {(int)response.StatusCode}, expected 401.");
            }
        }
    }

    public static TheoryData<string, string> InvalidTokens()
    {
        var claims = TestTokens.UserClaims(Guid.NewGuid());
        return new TheoryData<string, string>
        {
            { "expired", TestTokens.Create(claims, expiresUtc: DateTime.UtcNow.AddMinutes(-5)) },
            { "wrong signing key", TestTokens.Create(claims, signingKey: TestTokens.NewKey("attacker")) },
            { "unsigned (alg=none)", TestTokens.Create(claims, unsigned: true) },
            { "wrong audience", TestTokens.Create(claims, audience: "anon") },
            { "wrong issuer", TestTokens.Create(claims, issuer: "https://evil.example/auth/v1") },
            { "not a jwt", "not-a-jwt" },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Invalid_tokens_are_rejected_with_401(string reason, string token)
    {
        var client = factory.CreateClientWithToken(token);

        var get = await client.GetAsync("/api/companies");
        var post = await client.PostAsJsonAsync("/api/companies", new { name = "x" });

        Assert.True(get.StatusCode == HttpStatusCode.Unauthorized, $"GET with {reason}: {(int)get.StatusCode}");
        Assert.True(post.StatusCode == HttpStatusCode.Unauthorized, $"POST with {reason}: {(int)post.StatusCode}");
    }

    public static TheoryData<string, string> ValidTokensWithForbiddenIdentity()
    {
        var withoutSub = TestTokens.UserClaims(Guid.NewGuid());
        withoutSub.Remove("sub");

        var nonGuidSub = TestTokens.UserClaims(Guid.NewGuid());
        nonGuidSub["sub"] = "not-a-guid";

        var anonymous = TestTokens.UserClaims(Guid.NewGuid());
        anonymous["is_anonymous"] = true;

        return new TheoryData<string, string>
        {
            { "missing sub", TestTokens.Create(withoutSub) },
            { "sub is not a uuid", TestTokens.Create(nonGuidSub) },
            { "anonymous Supabase user", TestTokens.Create(anonymous) },
        };
    }

    [Theory]
    [MemberData(nameof(ValidTokensWithForbiddenIdentity))]
    public async Task Correctly_signed_tokens_without_a_valid_user_are_rejected_with_403(string reason, string token)
    {
        var client = factory.CreateClientWithToken(token);

        var get = await client.GetAsync("/api/companies");
        var post = await client.PostAsJsonAsync("/api/companies", new { name = "x" });

        Assert.True(get.StatusCode == HttpStatusCode.Forbidden, $"GET with {reason}: {(int)get.StatusCode}");
        Assert.True(post.StatusCode == HttpStatusCode.Forbidden, $"POST with {reason}: {(int)post.StatusCode}");
    }

    [Fact]
    public async Task A_valid_token_is_accepted()
    {
        var (_, client) = await factory.CreateUserClientAsync();

        var response = await client.GetAsync("/api/companies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
