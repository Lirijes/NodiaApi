using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodiaApi.Controllers;
using NodiaApi.Data;
using NodiaApi.Models;
using NodiaApi.Tests.Infrastructure;
using static NodiaApi.Tests.CompaniesTests;

namespace NodiaApi.Tests;

/// <summary>
/// Dataisolering: en användare får aldrig se eller påverka ett företag hen inte är medlem i,
/// och värden från klienten kan aldrig ge behörighet.
/// </summary>
[Collection(ApiCollection.Name)]
public class CompanyIsolationTests(NodiaApiFactory factory)
{
    [Fact]
    public async Task Another_user_cannot_read_the_company()
    {
        var (_, alice) = await factory.CreateUserClientAsync();
        var (_, bob) = await factory.CreateUserClientAsync();
        var company = await CreateCompanyAsync(alice, "Alice Secret AB");

        var get = await bob.GetAsync($"/api/companies/{company.Id}");
        var bobList = await bob.GetFromJsonAsync<List<CompanyResponse>>("/api/companies", NodiaApiFactory.Json);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.DoesNotContain("Alice Secret AB", await get.Content.ReadAsStringAsync());
        Assert.DoesNotContain(bobList!, c => c.Id == company.Id);
    }

    [Fact]
    public async Task Member_of_another_company_cannot_read_the_company()
    {
        var (_, alice) = await factory.CreateUserClientAsync();
        var (_, bob) = await factory.CreateUserClientAsync();
        var aliceCompany = await CreateCompanyAsync(alice, "Alice AB");
        await CreateCompanyAsync(bob, "Bob AB"); // Bob är Owner – men i ett ANNAT företag

        var response = await bob.GetAsync($"/api/companies/{aliceCompany.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Foreign_company_and_nonexistent_company_give_identical_responses()
    {
        var (_, alice) = await factory.CreateUserClientAsync();
        var (_, bob) = await factory.CreateUserClientAsync();
        var aliceCompany = await CreateCompanyAsync(alice, "Alice AB");

        var foreign = await bob.GetAsync($"/api/companies/{aliceCompany.Id}");
        var nonexistent = await bob.GetAsync($"/api/companies/{Guid.NewGuid()}");

        // Samma status och samma ProblemDetails (utom traceId) – går inte att se om företaget finns.
        Assert.Equal(nonexistent.StatusCode, foreign.StatusCode);
        Assert.Equal(await ProblemShapeAsync(nonexistent), await ProblemShapeAsync(foreign));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    [InlineData("1")]
    [InlineData("%27%20or%201%3D1--")] // ' or 1=1--
    public async Task Manipulated_company_id_gives_not_found(string companyId)
    {
        var (_, client) = await factory.CreateUserClientAsync();
        await CreateCompanyAsync(client, "Own AB");

        var response = await client.GetAsync($"/api/companies/{companyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Random_company_id_gives_not_found()
    {
        var (_, client) = await factory.CreateUserClientAsync();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Client_supplied_user_id_role_and_creator_are_ignored()
    {
        var (aliceId, alice) = await factory.CreateUserClientAsync();
        var (bobId, bob) = await factory.CreateUserClientAsync();

        // Alice försöker lägga företaget på Bob och sätta roller själv – i body, query och header.
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/companies?userId={bobId}&role=owner")
        {
            Content = JsonContent.Create(new
            {
                name = "Spoofed AB",
                userId = bobId,
                user_id = bobId,
                createdBy = bobId,
                role = "member",
                memberships = new[] { new { userId = bobId, role = "owner" } },
            }),
        };
        request.Headers.Add("X-User-Id", bobId.ToString());
        var response = await alice.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompanyResponse>(NodiaApiFactory.Json);
        Assert.Equal(CompanyRole.Owner, body!.Role);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.Include(c => c.Memberships).SingleAsync(c => c.Id == body.Id);
        Assert.Equal(aliceId, company.CreatedBy);
        var membership = Assert.Single(company.Memberships);
        Assert.Equal(aliceId, membership.UserId);
        Assert.Equal(CompanyRole.Owner, membership.Role);
        Assert.False(await db.CompanyMemberships.AnyAsync(m => m.UserId == bobId));

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/companies/{body.Id}")).StatusCode);
    }

    [Fact]
    public async Task User_id_in_query_or_header_does_not_change_whose_companies_are_listed()
    {
        var (aliceId, alice) = await factory.CreateUserClientAsync();
        var (_, bob) = await factory.CreateUserClientAsync();
        var aliceCompany = await CreateCompanyAsync(alice, "Alice AB");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/companies?userId={aliceId}");
        request.Headers.Add("X-User-Id", aliceId.ToString());
        var response = await bob.SendAsync(request);

        var list = await response.Content.ReadFromJsonAsync<List<CompanyResponse>>(NodiaApiFactory.Json);
        Assert.DoesNotContain(list!, c => c.Id == aliceCompany.Id);
    }

    private static async Task<string> ProblemShapeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        return $"{root.GetProperty("type")}|{root.GetProperty("title")}|{root.GetProperty("status")}";
    }
}
