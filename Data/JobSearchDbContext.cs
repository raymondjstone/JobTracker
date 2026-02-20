using JobTracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace JobTracker.Data;

public class JobSearchDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<JobListing> JobListings { get; set; }
    public DbSet<JobHistoryEntry> HistoryEntries { get; set; }
    public DbSet<JobRule> JobRules { get; set; }
    public DbSet<AppSettingsEntity> AppSettings { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public JobSearchDbContext(DbContextOptions<JobSearchDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(256).IsRequired();
            entity.Property(u => u.Name).HasMaxLength(256);
            entity.Property(u => u.PasswordHash).HasColumnType("nvarchar(max)");
            entity.Property(u => u.TwoFactorSecret).HasMaxLength(256);
            entity.Property(u => u.PasswordResetToken).HasMaxLength(256);
        });

        // JobListing configuration
        modelBuilder.Entity<JobListing>(entity =>
        {
            entity.HasKey(j => j.Id);
            entity.HasIndex(j => j.UserId);

            // All string columns use nvarchar(max) to avoid truncation issues
            entity.Property(j => j.Title).HasColumnType("nvarchar(max)");
            entity.Property(j => j.Company).HasColumnType("nvarchar(max)");
            entity.Property(j => j.Location).HasColumnType("nvarchar(max)");
            entity.Property(j => j.Description).HasColumnType("nvarchar(max)");
            entity.Property(j => j.Url).HasColumnType("nvarchar(max)");
            entity.Property(j => j.Salary).HasColumnType("nvarchar(max)");
            entity.Property(j => j.Source).HasColumnType("nvarchar(max)");
            entity.Property(j => j.Notes).HasColumnType("nvarchar(max)");
            entity.Property(j => j.CoverLetter).HasColumnType("nvarchar(max)");

            entity.Property(j => j.SalaryMin).HasColumnType("decimal(18,2)");
            entity.Property(j => j.SalaryMax).HasColumnType("decimal(18,2)");

            // List<string> Skills -> JSON column
            entity.Property(j => j.Skills).HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>()
            ).HasColumnType("nvarchar(max)");

            // List<ApplicationStageChange> StageHistory -> JSON column
            entity.Property(j => j.StageHistory).HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<ApplicationStageChange>>(v, JsonOptions) ?? new List<ApplicationStageChange>()
            ).HasColumnType("nvarchar(max)");
        });

        // JobHistoryEntry configuration
        modelBuilder.Entity<JobHistoryEntry>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.HasIndex(h => h.UserId);

            entity.Property(h => h.JobTitle).HasColumnType("nvarchar(max)");
            entity.Property(h => h.Company).HasColumnType("nvarchar(max)");
            entity.Property(h => h.JobUrl).HasColumnType("nvarchar(max)");
            entity.Property(h => h.OldValue).HasColumnType("nvarchar(max)");
            entity.Property(h => h.NewValue).HasColumnType("nvarchar(max)");
            entity.Property(h => h.Details).HasColumnType("nvarchar(max)");
            entity.Property(h => h.RuleName).HasColumnType("nvarchar(max)");

            // Index on JobId for quick lookups (NOT a foreign key - job may be deleted)
            entity.HasIndex(h => h.JobId);
            entity.HasIndex(h => h.Timestamp);
        });

        // JobRule configuration
        modelBuilder.Entity<JobRule>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.UserId);
            entity.Property(r => r.Name).HasColumnType("nvarchar(max)");
            entity.Property(r => r.Value).HasColumnType("nvarchar(max)");

            // List<RuleCondition> Conditions -> JSON column
            entity.Property(r => r.Conditions).HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<RuleCondition>>(v, JsonOptions) ?? new List<RuleCondition>()
            ).HasColumnType("nvarchar(max)");

            // HasCompoundConditions is a computed property, ignore it
            entity.Ignore(r => r.HasCompoundConditions);
        });

        // AppSettingsEntity configuration (per-user settings)
        modelBuilder.Entity<AppSettingsEntity>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.UserId).IsUnique();  // One settings record per user
            entity.Property(s => s.LinkedInUrl).HasColumnType("nvarchar(max)");
            entity.Property(s => s.S1JobsUrl).HasColumnType("nvarchar(max)");
            entity.Property(s => s.IndeedUrl).HasColumnType("nvarchar(max)");
            entity.Property(s => s.WTTJUrl).HasColumnType("nvarchar(max)");
        });
    }
}
