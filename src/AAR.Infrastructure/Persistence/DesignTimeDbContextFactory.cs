// =============================================================================
// AAR.Infrastructure - Persistence/DesignTimeDbContextFactory.cs
// Design-time factory for EF Core migrations
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AAR.Infrastructure.Persistence;

/// <summary>
/// Factory for creating DbContext at design time for EF Core migrations
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AarDbContext>
{
    public AarDbContext CreateDbContext(string[] args)
    {
        // Use a hardcoded connection string for design-time migrations
        var connectionString = "Server=localhost;Database=AAR;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

        var optionsBuilder = new DbContextOptionsBuilder<AarDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(typeof(AarDbContext).Assembly.FullName);
            sqlOptions.EnableRetryOnFailure(3);
        });

        return new AarDbContext(optionsBuilder.Options);
    }
}
