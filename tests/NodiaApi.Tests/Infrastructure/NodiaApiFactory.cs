using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;
using NodiaApi.Data;
using Testcontainers.PostgreSql;

namespace NodiaApi.Tests.Infrastructure;

/// <summary>
/// Startar hela API:t i minnet mot en riktig PostgreSQL i Docker (Testcontainers).
///
/// - Miljön är "Testing": user-secrets (produktionens connection string) laddas bara
///   i Development och kan därför aldrig användas av testerna.
/// - Databasen förbereds som Supabase (auth.users, rollerna anon/authenticated och
///   Supabase ÖPPNA default privileges) och sedan körs den riktiga EF-migrationen.
/// - JWT: enbart nyckelkällan byts ut (testnyckel i stället för Supabase JWKS).
///   TokenValidationParameters lämnas orörda – se JwtValidationTests.
/// </summary>
public sealed class NodiaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithUsername("postgres") // samma rollnamn som backend använder i Supabase
        .WithPassword("postgres")
        .WithDatabase("nodia_test")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Det Supabase tillhandahåller och som migrationen förutsätter.</summary>
    private const string SupabaseStubSql = """
        create role anon nologin;
        create role authenticated nologin;
        create role service_role nologin bypassrls;
        grant usage on schema public to anon, authenticated, service_role;

        create schema auth;
        create table auth.users (id uuid primary key);

        -- Samma ÖPPNA standard som Supabase har, så att testerna visar att
        -- migrationen verkligen stänger åtkomsten.
        alter default privileges for role postgres in schema public
            grant all on tables to anon, authenticated, service_role;
        alter default privileges for role postgres in schema public
            grant all on sequences to anon, authenticated, service_role;
        """;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await ExecuteSqlAsync(SupabaseStubSql);

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Supabase", ConnectionString);

        builder.ConfigureTestServices(services =>
            // Körs EFTER produktionens JwtBearer-konfiguration. Ersätter bara
            // nyckelkällan (Supabase JWKS -> testnyckel); valideringsreglerna är orörda.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration
                    {
                        Issuer = TestTokens.Issuer,
                        SigningKeys = { TestTokens.SigningKey },
                    })));
    }

    /// <summary>Skapar en användare i (stubbade) auth.users, som Supabase Auth gör vid signup.</summary>
    public async Task<Guid> CreateAuthUserAsync()
    {
        var userId = Guid.NewGuid();
        await ExecuteSqlAsync("insert into auth.users (id) values (@id)", ("id", userId));
        return userId;
    }

    /// <summary>Ny användare + HttpClient med en giltig token för just den användaren.</summary>
    public async Task<(Guid UserId, HttpClient Client)> CreateUserClientAsync()
    {
        var userId = await CreateAuthUserAsync();
        return (userId, CreateClientWithToken(TestTokens.ForUser(userId)));
    }

    public HttpClient CreateClientWithToken(string? token)
    {
        var client = CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task ExecuteSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<NodiaApiFactory>
{
    public const string Name = "Api";
}
