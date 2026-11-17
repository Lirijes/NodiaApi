using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodiaApi.Controllers;
using NodiaApi.Data;
using NodiaApi.Models;
using NodiaApi.Tests.Infrastructure;

namespace NodiaApi.Tests;

/// <summary>Skapa, lista och hämta företag som den inloggade användaren är medlem i.</summary>
[Collection(ApiCollection.Name)]
public class CompaniesTests(NodiaApiFactory factory)
{
    [Fact]
    public async Task Create_makes_the_caller_the_only_member_with_role_owner()
    {
        var (userId, client) = await factory.CreateUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/companies", new { name = "  Acme AB  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse>(NodiaApiFactory.Json);
        Assert.NotNull(body);
        Assert.Equal("Acme AB", body.Name); // trimmat
        Assert.Equal(CompanyRole.Owner, body.Role);
        Assert.Equal($"/api/companies/{body.Id}", response.Headers.Location?.AbsolutePath);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.Include(c => c.Memberships).SingleAsync(c => c.Id == body.Id);
        Assert.Equal(userId, company.CreatedBy);
        var membership = Assert.Single(company.Memberships);
        Assert.Equal(userId, membership.UserId);
        Assert.Equal(CompanyRole.Owner, membership.Role);
    }

    [Fact]
    public async Task Role_is_serialized_as_lowercase_string()
    {
        var (_, client) = await factory.CreateUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/companies", new { name = "Json AB" });

        Assert.Contains("\"role\":\"owner\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task List_returns_exactly_the_callers_own_companies()
    {
        var (_, alice) = await factory.CreateUserClientAsync();
        var (_, bob) = await factory.CreateUserClientAsync();
        var a1 = await CreateCompanyAsync(alice, "Alice One");
        var a2 = await CreateCompanyAsync(alice, "Alice Two");
        var b1 = await CreateCompanyAsync(bob, "Bob One");

        var aliceList = await alice.GetFromJsonAsync<List<CompanyResponse>>("/api/companies", NodiaApiFactory.Json);
        var bobList = await bob.GetFromJsonAsync<List<CompanyResponse>>("/api/companies", NodiaApiFactory.Json);

        Assert.Equal(new[] { a1.Id, a2.Id }.Order(), aliceList!.Select(c => c.Id).Order());
        Assert.Equal([b1.Id], bobList!.Select(c => c.Id));
    }

    [Fact]
    public async Task List_is_empty_for_a_new_user()
    {
        var (_, client) = await factory.CreateUserClientAsync();

        var list = await client.GetFromJsonAsync<List<CompanyResponse>>("/api/companies", NodiaApiFactory.Json);

        Assert.Empty(list!);
    }

    [Fact]
    public async Task Get_returns_own_company()
    {
        var (_, client) = await factory.CreateUserClientAsync();
        var created = await CreateCompanyAsync(client, "Own AB");

        var company = await client.GetFromJsonAsync<CompanyResponse>($"/api/companies/{created.Id}", NodiaApiFactory.Json);

        Assert.Equal(created, company);
    }

    [Fact]
    public async Task A_plain_member_can_read_the_company_with_role_member()
    {
        var (_, owner) = await factory.CreateUserClientAsync();
        var (memberId, member) = await factory.CreateUserClientAsync();
        var company = await CreateCompanyAsync(owner, "Shared AB");
        // Det finns ännu ingen endpoint för att bjuda in – medlemskapet läggs in direkt i databasen.
        await factory.ExecuteSqlAsync(
            "insert into public.company_memberships (company_id, user_id, role) values (@c, @u, 'member')",
            ("c", company.Id), ("u", memberId));

        var fetched = await member.GetFromJsonAsync<CompanyResponse>($"/api/companies/{company.Id}", NodiaApiFactory.Json);
        var list = await member.GetFromJsonAsync<List<CompanyResponse>>("/api/companies", NodiaApiFactory.Json);

        Assert.Equal(CompanyRole.Member, fetched!.Role);
        Assert.Equal(CompanyRole.Member, Assert.Single(list!).Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Create_rejects_missing_or_blank_name(string? name)
    {
        var (_, client) = await factory.CreateUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/companies", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_name_longer_than_100_characters_but_accepts_100()
    {
        var (_, client) = await factory.CreateUserClientAsync();

        var tooLong = await client.PostAsJsonAsync("/api/companies", new { name = new string('x', 101) });
        var exactly100 = await client.PostAsJsonAsync("/api/companies", new { name = new string('x', 100) });

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, exactly100.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_missing_body()
    {
        var (_, client) = await factory.CreateUserClientAsync();

        var response = await client.PostAsync("/api/companies",
            new StringContent("", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_returns_401_when_the_auth_user_has_been_deleted()
    {
        // Token är fortfarande giltig (upp till 1 h), men användaren finns inte längre i auth.users.
        var client = factory.CreateClientWithToken(TestTokens.ForUser(Guid.NewGuid()));

        var response = await client.PostAsJsonAsync("/api/companies", new { name = "Ghost AB" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static async Task<CompanyResponse> CreateCompanyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/companies", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompanyResponse>(NodiaApiFactory.Json))!;
    }
}
