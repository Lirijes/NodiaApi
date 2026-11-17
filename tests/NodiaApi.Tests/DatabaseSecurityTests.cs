using System.Net.Http.Json;
using Npgsql;
using NodiaApi.Controllers;
using NodiaApi.Tests.Infrastructure;
using static NodiaApi.Tests.CompaniesTests;

namespace NodiaApi.Tests;

/// <summary>
/// Kör mot testdatabasen efter den RIKTIGA migrationen och visar att den stänger
/// Supabase Data API-vägen (anon/authenticated) och kopplar korrekt till auth.users.
/// </summary>
[Collection(ApiCollection.Name)]
public class DatabaseSecurityTests(NodiaApiFactory factory)
{
    private static readonly string[] Tables = ["companies", "company_memberships", "__EFMigrationsHistory"];

    [Fact]
    public async Task Rls_is_enabled_without_policies_on_all_tables()
    {
        foreach (var table in Tables)
        {
            var rls = await ScalarAsync<bool>(
                "select relrowsecurity from pg_class where oid = to_regclass(@t)", ("t", $"public.\"{table}\""));
            var policies = await ScalarAsync<long>(
                "select count(*) from pg_policies where schemaname = 'public' and tablename = @t", ("t", table));

            Assert.True(rls, $"RLS is not enabled on {table}");
            Assert.Equal(0, policies);
        }
    }

    [Theory]
    [InlineData("anon")]
    [InlineData("authenticated")]
    public async Task Data_api_roles_have_no_access_to_the_tables(string role)
    {
        foreach (var table in Tables)
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                ScalarInRolledBackTransactionAsync<long>(
                    setupSql: $"set local role {role}",
                    sql: $"select count(*) from public.\"{table}\""));

            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        }
    }

    [Fact]
    public async Task New_tables_do_not_inherit_privileges_for_data_api_roles()
    {
        // Skapas i en transaktion som rullas tillbaka (testdatabasen – inte Supabase).
        var grants = await ScalarInRolledBackTransactionAsync<long>(
            setupSql: "create table public._acl_probe (x int)",
            sql: """
                select count(*) from information_schema.role_table_grants
                where table_schema = 'public' and table_name = '_acl_probe' and grantee in ('anon', 'authenticated')
                """);

        Assert.Equal(0, grants);
    }

    [Fact]
    public async Task Deleting_an_auth_user_removes_memberships_and_clears_created_by()
    {
        var (userId, client) = await factory.CreateUserClientAsync();
        var company = await CreateCompanyAsync(client, "Soon Orphaned AB");

        await factory.ExecuteSqlAsync("delete from auth.users where id = @id", ("id", userId));

        Assert.Equal(0, await ScalarAsync<long>(
            "select count(*) from public.company_memberships where user_id = @u", ("u", userId)));
        Assert.True(await ScalarAsync<bool>(
            "select created_by is null from public.companies where id = @c", ("c", company.Id)));
    }

    [Fact]
    public async Task Membership_role_must_be_owner_or_member()
    {
        var (userId, client) = await factory.CreateUserClientAsync();
        var company = await CreateCompanyAsync(client, "Check AB");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => factory.ExecuteSqlAsync(
            "update public.company_memberships set role = 'admin' where company_id = @c and user_id = @u",
            ("c", company.Id), ("u", userId)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Kör setupSql och sql i samma transaktion, som alltid rullas tillbaka.</summary>
    private async Task<T> ScalarInRolledBackTransactionAsync<T>(string setupSql, string sql)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var setup = new NpgsqlCommand(setupSql, connection, transaction))
                await setup.ExecuteNonQueryAsync();
            await using var query = new NpgsqlCommand(sql, connection, transaction);
            return (T)(await query.ExecuteScalarAsync())!;
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
