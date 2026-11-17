using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NodiaApi.Auth;
using NodiaApi.Data;
using NodiaApi.Tests.Infrastructure;

namespace NodiaApi.Tests;

/// <summary>
/// Arkitektur- och konfigurationstester: fångar en ny endpoint eller ändrad konfiguration
/// som skulle kringgå säkerheten – även om ingen skriver ett test för just den endpointen.
/// </summary>
[Collection(ApiCollection.Name)]
public class SecurityConfigurationTests(NodiaApiFactory factory)
{
    private IReadOnlyList<RouteEndpoint> Endpoints =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    [Fact]
    public void Every_endpoint_with_companyId_in_the_route_requires_company_membership()
    {
        var companyEndpoints = Endpoints.Where(e => e.RoutePattern.RawText!.Contains("{companyId")).ToList();

        Assert.NotEmpty(companyEndpoints);
        Assert.All(companyEndpoints, e =>
            Assert.True(e.Metadata.GetMetadata<CompanyMemberAttribute>() is not null,
                $"'{e.DisplayName}' has {{companyId}} in its route but no [CompanyMember]."));
    }

    [Fact]
    public void Only_HealthController_allows_anonymous_access()
    {
        var anonymous = Endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerName)
            .Distinct();

        Assert.Equal(["Health"], anonymous);
    }

    [Fact]
    public void Fallback_and_default_policies_require_an_authenticated_user()
    {
        var options = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(options.DefaultPolicy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.Same(options.DefaultPolicy, options.FallbackPolicy);
    }

    [Fact]
    public void Jwt_validation_rules_are_identical_to_production_only_the_key_source_is_replaced()
    {
        var supabaseUrl = factory.Services.GetRequiredService<IConfiguration>()["Supabase:Url"];
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var tvp = options.TokenValidationParameters;

        // Produktionens regler (Program.cs) – oförändrade i testvärden.
        Assert.Equal($"{supabaseUrl}/auth/v1", options.Authority);
        Assert.Equal(TestTokens.Issuer, tvp.ValidIssuer);
        Assert.False(options.MapInboundClaims);
        Assert.True(tvp.ValidateIssuerSigningKey);
        Assert.True(tvp.ValidateIssuer);
        Assert.True(tvp.ValidateAudience);
        Assert.Equal("authenticated", tvp.ValidAudience);
        Assert.True(tvp.ValidateLifetime);
        Assert.True(tvp.RequireSignedTokens);
        Assert.True(tvp.RequireExpirationTime);
        Assert.Equal(TimeSpan.FromSeconds(30), tvp.ClockSkew);
        Assert.Equal("sub", tvp.NameClaimType);
        Assert.Equal("role", tvp.RoleClaimType);
        // Inga nycklar är hårdkodade i parametrarna – de kommer från ConfigurationManager.
        Assert.Null(tvp.IssuerSigningKey);
        Assert.Null(tvp.IssuerSigningKeys);

        // Det enda testet byter ut: nyckelkällan (Supabase JWKS -> statisk testnyckel).
        Assert.IsType<StaticConfigurationManager<OpenIdConnectConfiguration>>(options.ConfigurationManager);
    }

    [Fact]
    public void Tests_use_the_test_container_and_never_the_production_database()
    {
        using var scope = factory.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();

        Assert.Equal(factory.ConnectionString, connectionString);
        Assert.DoesNotContain("supabase", connectionString, StringComparison.OrdinalIgnoreCase);
    }
}
