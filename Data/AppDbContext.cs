using Microsoft.EntityFrameworkCore;

namespace NodiaApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Add your tables here, e.g.:
    // public DbSet<Customer> Customers => Set<Customer>();
}
