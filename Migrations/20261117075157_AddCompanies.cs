using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NodiaApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.CheckConstraint("ck_companies_name_not_blank", "btrim(name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "company_memberships",
                schema: "public",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_memberships", x => new { x.company_id, x.user_id });
                    table.CheckConstraint("ck_company_memberships_role", "role in ('owner', 'member')");
                    table.ForeignKey(
                        name: "fk_company_memberships_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "public",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_memberships_user_id",
                schema: "public",
                table: "company_memberships",
                column: "user_id");

            // ------------------------------------------------------------------
            // 1) Främmande nycklar mot Supabase Auth (auth.users).
            //    EF kan inte modellera auth-schemat, därför rå SQL.
            //    - Raderas en användare försvinner hens medlemskap (cascade).
            //    - created_by är bara historik och nollställs (set null).
            // ------------------------------------------------------------------
            migrationBuilder.Sql("""
                alter table public.company_memberships
                    add constraint fk_company_memberships_auth_users_user_id
                    foreign key (user_id) references auth.users (id) on delete cascade;

                alter table public.companies
                    add constraint fk_companies_auth_users_created_by
                    foreign key (created_by) references auth.users (id) on delete set null;
                """);

            // ------------------------------------------------------------------
            // 2) Stäng Supabase Data API (PostgREST) för dessa tabeller.
            //    Backend ansluter som "postgres" (BYPASSRLS) och påverkas inte.
            //    - RLS på utan policies = deny-all för anon/authenticated.
            //    - REVOKE tar dessutom bort själva tabellrättigheterna, så
            //      skyddet håller även om någon av misstag skulle lägga till en policy.
            //    - __EFMigrationsHistory skapas av EF innan migrationen körs och
            //      har därför fått Supabase standardrättigheter – den stängs också.
            // ------------------------------------------------------------------
            migrationBuilder.Sql("""
                alter table public.companies enable row level security;
                alter table public.company_memberships enable row level security;
                alter table public."__EFMigrationsHistory" enable row level security;

                revoke all on table public.companies from anon, authenticated;
                revoke all on table public.company_memberships from anon, authenticated;
                revoke all on table public."__EFMigrationsHistory" from anon, authenticated;
                """);

            // ------------------------------------------------------------------
            // 3) Framtida tabeller och sekvenser som rollen postgres skapar i public
            //    ska INTE automatiskt få rättigheter för anon/authenticated.
            //    Ändrar bara standardvärdet för nya objekt – befintliga påverkas inte.
            //    (Varje ny tabell ska ändå få "enable row level security" i sin migration.)
            // ------------------------------------------------------------------
            migrationBuilder.Sql("""
                alter default privileges for role postgres in schema public
                    revoke all on tables from anon, authenticated;

                alter default privileges for role postgres in schema public
                    revoke all on sequences from anon, authenticated;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Återställer Supabase standardrättigheter så att Down är en exakt
            // spegling av Up. OBS: efter Down blir nya tabeller i public åter
            // åtkomliga för anon/authenticated som standard.
            migrationBuilder.Sql("""
                alter default privileges for role postgres in schema public
                    grant all on tables to anon, authenticated;

                alter default privileges for role postgres in schema public
                    grant all on sequences to anon, authenticated;
                """);

            // FK:erna mot auth.users, RLS och rättigheterna försvinner med tabellerna.
            migrationBuilder.DropTable(
                name: "company_memberships",
                schema: "public");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "public");
        }
    }
}
