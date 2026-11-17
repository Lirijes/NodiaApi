using Microsoft.EntityFrameworkCore;
using NodiaApi.Models;

namespace NodiaApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Explicit schema i all genererad SQL – oberoende av anslutningens search_path.
        modelBuilder.HasDefaultSchema("public");

        // Tabell- och kolumnnamn i snake_case, som är konventionen i Postgres/Supabase.
        // Främmande nycklar mot auth.users kan EF inte modellera (Supabase äger auth-schemat),
        // så de läggs till med rå SQL i migrationen.

        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("companies", t =>
                t.HasCheckConstraint("ck_companies_name_not_blank", "btrim(name) <> ''"));

            e.HasKey(c => c.Id).HasName("pk_companies");

            e.Property(c => c.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            e.Property(c => c.Name)
                .HasColumnName("name")
                .HasMaxLength(100)
                .IsRequired();

            e.Property(c => c.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            e.Property(c => c.CreatedBy)
                .HasColumnName("created_by");
        });

        modelBuilder.Entity<CompanyMembership>(e =>
        {
            e.ToTable("company_memberships", t =>
                t.HasCheckConstraint("ck_company_memberships_role", "role in ('owner', 'member')"));

            // Sammansatt PK: en användare kan bara ha ETT medlemskap per företag.
            // Fungerar också som index för frågan "är user X medlem i company Y?".
            e.HasKey(m => new { m.CompanyId, m.UserId }).HasName("pk_company_memberships");

            e.Property(m => m.CompanyId).HasColumnName("company_id");
            e.Property(m => m.UserId).HasColumnName("user_id");

            // Lagras som "owner"/"member" (matchar CHECK-constrainten ovan).
            e.Property(m => m.Role)
                .HasColumnName("role")
                .HasConversion(
                    role => role.ToString().ToLowerInvariant(),
                    value => Enum.Parse<CompanyRole>(value, ignoreCase: true))
                .IsRequired();

            e.Property(m => m.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            // "Vilka företag är jag medlem i?" – snabb uppslagning per användare.
            e.HasIndex(m => m.UserId).HasDatabaseName("ix_company_memberships_user_id");

            // Raderas ett företag försvinner dess medlemskap.
            e.HasOne(m => m.Company)
                .WithMany(c => c.Memberships)
                .HasForeignKey(m => m.CompanyId)
                .HasConstraintName("fk_company_memberships_companies_company_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
