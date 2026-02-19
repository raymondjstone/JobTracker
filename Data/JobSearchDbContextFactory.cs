using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobTracker.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// This allows the dotnet ef commands to create the DbContext without running the app.
/// </summary>
public class JobSearchDbContextFactory : IDesignTimeDbContextFactory<JobSearchDbContext>
{
    public JobSearchDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("JobSearchDb");

        var optionsBuilder = new DbContextOptionsBuilder<JobSearchDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new JobSearchDbContext(optionsBuilder.Options);
    }
}
