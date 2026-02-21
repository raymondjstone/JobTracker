using JobTracker.Data;
using JobTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Services;

public class SqlServerStorageBackend : IStorageBackend
{
    private readonly IDbContextFactory<JobSearchDbContext> _factory;
    private readonly ILogger<SqlServerStorageBackend> _logger;
    private readonly int _historyMax;

    public SqlServerStorageBackend(IDbContextFactory<JobSearchDbContext> factory, ILogger<SqlServerStorageBackend> logger, IConfiguration configuration)
    {
        _factory = factory;
        _logger = logger;
        _historyMax = configuration.GetValue<int>("HistoryMax", 50000);
    }

    // User operations
    public User? GetUserById(Guid id)
    {
        using var db = _factory.CreateDbContext();
        return db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
    }

    public User? GetUserByEmail(string email)
    {
        using var db = _factory.CreateDbContext();
        return db.Users.AsNoTracking().FirstOrDefault(u => u.Email.ToLower() == email.ToLower());
    }

    public User? GetUserByResetToken(string token)
    {
        using var db = _factory.CreateDbContext();
        return db.Users.AsNoTracking().FirstOrDefault(u => u.PasswordResetToken == token);
    }

    public List<User> GetAllUsers()
    {
        using var db = _factory.CreateDbContext();
        return db.Users.AsNoTracking().ToList();
    }

    public void SaveUser(User user)
    {
        using var db = _factory.CreateDbContext();
        db.Users.Update(user);
        db.SaveChanges();
    }

    public void AddUser(User user)
    {
        using var db = _factory.CreateDbContext();
        db.Users.Add(user);
        db.SaveChanges();
        _logger.LogInformation("Added user: {Email}", user.Email);
    }

    public void DeleteUser(Guid id)
    {
        using var db = _factory.CreateDbContext();
        db.Users.Where(u => u.Id == id).ExecuteDelete();
    }

    public List<JobListing> LoadJobs(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        var jobs = db.JobListings.AsNoTracking().Where(j => j.UserId == userId).ToList();
        _logger.LogInformation("Loaded {Count} jobs from SQL Server for user {UserId}", jobs.Count, userId);
        return jobs;
    }

    public void SaveJobs(List<JobListing> jobs, Guid userId)
    {
        using var db = _factory.CreateDbContext();

        var existingIds = db.JobListings.Where(j => j.UserId == userId).Select(j => j.Id).ToHashSet();
        var incomingIds = jobs.Select(j => j.Id).ToHashSet();

        // Delete removed jobs
        var toDelete = existingIds.Except(incomingIds).ToList();
        if (toDelete.Count > 0)
        {
            db.JobListings.Where(j => toDelete.Contains(j.Id)).ExecuteDelete();
        }

        // Separate new vs existing
        var toAdd = jobs.Where(j => !existingIds.Contains(j.Id)).ToList();
        var toUpdate = jobs.Where(j => existingIds.Contains(j.Id)).ToList();

        if (toAdd.Count > 0)
        {
            db.JobListings.AddRange(toAdd);
        }

        foreach (var job in toUpdate)
        {
            db.JobListings.Update(job);
        }

        db.SaveChanges();
        _logger.LogDebug("Saved {Count} jobs to SQL Server for user {UserId} (added={Added}, updated={Updated}, deleted={Deleted})",
            jobs.Count, userId, toAdd.Count, toUpdate.Count, toDelete.Count);
    }

    public void SaveJob(JobListing job)
    {
        using var db = _factory.CreateDbContext();
        db.JobListings.Update(job);
        db.SaveChanges();
    }

    public void AddJob(JobListing job)
    {
        using var db = _factory.CreateDbContext();
        db.JobListings.Add(job);
        db.SaveChanges();
    }

    public void DeleteJob(Guid id)
    {
        using var db = _factory.CreateDbContext();
        db.JobListings.Where(j => j.Id == id).ExecuteDelete();
    }

    public void DeleteAllJobs(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        db.JobListings.Where(j => j.UserId == userId).ExecuteDelete();
    }

    public List<JobHistoryEntry> LoadHistory(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        var history = db.HistoryEntries
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.Timestamp)
            .ToList();
        _logger.LogInformation("Loaded {Count} history entries from SQL Server for user {UserId}", history.Count, userId);
        return history;
    }

    public void SaveHistory(List<JobHistoryEntry> history, Guid userId)
    {
        using var db = _factory.CreateDbContext();

        var existingIds = db.HistoryEntries.Where(h => h.UserId == userId).Select(h => h.Id).ToHashSet();

        // Append-only: only add entries that don't exist yet
        var toAdd = history.Where(h => !existingIds.Contains(h.Id)).ToList();

        if (toAdd.Count > 0)
        {
            db.HistoryEntries.AddRange(toAdd);
            db.SaveChanges();
        }

        // Enforce 50k cap per user
        var totalCount = db.HistoryEntries.Where(h => h.UserId == userId).Count();
        if (totalCount > _historyMax)
        {
            var idsToRemove = db.HistoryEntries
                .Where(h => h.UserId == userId)
                .OrderBy(h => h.Timestamp)
                .Take(totalCount - _historyMax)
                .Select(h => h.Id)
                .ToList();
            db.HistoryEntries.Where(h => idsToRemove.Contains(h.Id)).ExecuteDelete();
        }

        _logger.LogDebug("Saved history to SQL Server for user {UserId} (appended={Added})", userId, toAdd.Count);
    }

    public void AddHistoryEntry(JobHistoryEntry entry)
    {
        using var db = _factory.CreateDbContext();
        db.HistoryEntries.Add(entry);
        db.SaveChanges();

        // Enforce 50k cap per user
        var totalCount = db.HistoryEntries.Where(h => h.UserId == entry.UserId).Count();
        if (totalCount > _historyMax)
        {
            db.HistoryEntries
                .Where(h => h.UserId == entry.UserId)
                .OrderBy(h => h.Timestamp)
                .Take(totalCount - _historyMax)
                .ExecuteDelete();
        }
    }

    public void DeleteAllHistory(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        db.HistoryEntries.Where(h => h.UserId == userId).ExecuteDelete();
    }

    public AppSettings LoadSettings(Guid userId)
    {
        using var db = _factory.CreateDbContext();

        var entity = db.AppSettings.AsNoTracking().FirstOrDefault(s => s.UserId == userId);
        var rules = db.JobRules.AsNoTracking().Where(r => r.UserId == userId).ToList();

        var settings = new AppSettings();

        if (entity != null)
        {
            settings.JobSiteUrls = new JobSiteUrls
            {
                LinkedIn = entity.LinkedInUrl,
                S1Jobs = entity.S1JobsUrl,
                Indeed = entity.IndeedUrl,
                WTTJ = entity.WTTJUrl
            };
            settings.JobRules = new JobRulesSettings
            {
                EnableAutoRules = entity.EnableAutoRules,
                StopOnFirstMatch = entity.StopOnFirstMatch,
                Rules = rules
            };
        }
        else
        {
            settings.JobRules.Rules = rules;
        }

        _logger.LogInformation("Loaded settings from SQL Server for user {UserId} ({RuleCount} rules)", userId, rules.Count);
        return settings;
    }

    public void SaveSettings(AppSettings settings, Guid userId)
    {
        using var db = _factory.CreateDbContext();

        // Upsert AppSettingsEntity for this user
        var entity = db.AppSettings.FirstOrDefault(s => s.UserId == userId);
        if (entity == null)
        {
            entity = new AppSettingsEntity { UserId = userId };
            db.AppSettings.Add(entity);
        }

        entity.LinkedInUrl = settings.JobSiteUrls.LinkedIn;
        entity.S1JobsUrl = settings.JobSiteUrls.S1Jobs;
        entity.IndeedUrl = settings.JobSiteUrls.Indeed;
        entity.WTTJUrl = settings.JobSiteUrls.WTTJ;
        entity.EnableAutoRules = settings.JobRules.EnableAutoRules;
        entity.StopOnFirstMatch = settings.JobRules.StopOnFirstMatch;

        // Sync rules for this user: diff-based
        var existingRules = db.JobRules.Where(r => r.UserId == userId).ToList();
        var existingRuleIds = existingRules.Select(r => r.Id).ToHashSet();
        var incomingRuleIds = settings.JobRules.Rules.Select(r => r.Id).ToHashSet();

        // Delete removed rules
        var rulesToDelete = existingRules.Where(r => !incomingRuleIds.Contains(r.Id)).ToList();
        if (rulesToDelete.Count > 0)
        {
            db.JobRules.RemoveRange(rulesToDelete);
        }

        // Upsert incoming rules
        foreach (var rule in settings.JobRules.Rules)
        {
            rule.UserId = userId;
            if (existingRuleIds.Contains(rule.Id))
            {
                // Update rule already tracked from the user query
                var existing = existingRules.First(r => r.Id == rule.Id);
                db.Entry(existing).CurrentValues.SetValues(rule);
                existing.Conditions = rule.Conditions;
            }
            else
            {
                // Check if rule exists under a different UserId (prevents PK violation)
                var existingInDb = db.JobRules.Find(rule.Id);
                if (existingInDb != null)
                {
                    db.Entry(existingInDb).CurrentValues.SetValues(rule);
                    existingInDb.Conditions = rule.Conditions;
                }
                else
                {
                    db.JobRules.Add(rule);
                }
            }
        }

        db.SaveChanges();
        _logger.LogDebug("Saved settings to SQL Server for user {UserId} ({RuleCount} rules)", userId, settings.JobRules.Rules.Count);
    }

    /// <summary>
    /// Whether the database has any existing data (used for import detection)
    /// </summary>
    public bool HasExistingData()
    {
        using var db = _factory.CreateDbContext();
        return db.JobListings.Any() || db.HistoryEntries.Any() || db.JobRules.Any();
    }

    /// <summary>
    /// Migrate existing data (with empty UserId) to the specified user
    /// </summary>
    public void MigrateExistingDataToUser(Guid userId)
    {
        using var db = _factory.CreateDbContext();

        // Migrate jobs with empty UserId
        var emptyGuid = Guid.Empty;
        var jobsToMigrate = db.JobListings.Where(j => j.UserId == emptyGuid).ToList();
        foreach (var job in jobsToMigrate)
        {
            job.UserId = userId;
        }

        // Migrate history entries
        var historyToMigrate = db.HistoryEntries.Where(h => h.UserId == emptyGuid).ToList();
        foreach (var entry in historyToMigrate)
        {
            entry.UserId = userId;
        }

        // Migrate rules
        var rulesToMigrate = db.JobRules.Where(r => r.UserId == emptyGuid).ToList();
        foreach (var rule in rulesToMigrate)
        {
            rule.UserId = userId;
        }

        // Migrate settings - find old settings and assign to user
        var oldSettings = db.AppSettings.ToList();
        foreach (var setting in oldSettings)
        {
            if (setting.UserId == emptyGuid)
            {
                setting.UserId = userId;
            }
        }

        db.SaveChanges();
        _logger.LogInformation("Migrated existing data to user {UserId}: {Jobs} jobs, {History} history entries, {Rules} rules",
            userId, jobsToMigrate.Count, historyToMigrate.Count, rulesToMigrate.Count);
    }

    /// <summary>
    /// Bulk import data from another storage backend (used for JSON->SQL migration)
    /// </summary>
    public void ImportFrom(IStorageBackend source, Guid userId)
    {
        using var db = _factory.CreateDbContext();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        var jobs = source.LoadJobs(userId);
        foreach (var job in jobs)
        {
            job.UserId = userId;
        }
        if (jobs.Count > 0)
        {
            db.JobListings.AddRange(jobs);
            _logger.LogInformation("Importing {Count} jobs to SQL Server for user {UserId}", jobs.Count, userId);
        }

        var history = source.LoadHistory(userId);
        foreach (var entry in history)
        {
            entry.UserId = userId;
        }
        if (history.Count > 0)
        {
            db.HistoryEntries.AddRange(history);
            _logger.LogInformation("Importing {Count} history entries to SQL Server for user {UserId}", history.Count, userId);
        }

        var settings = source.LoadSettings(userId);
        var entity = new AppSettingsEntity
        {
            UserId = userId,
            LinkedInUrl = settings.JobSiteUrls.LinkedIn,
            S1JobsUrl = settings.JobSiteUrls.S1Jobs,
            IndeedUrl = settings.JobSiteUrls.Indeed,
            WTTJUrl = settings.JobSiteUrls.WTTJ,
            EnableAutoRules = settings.JobRules.EnableAutoRules,
            StopOnFirstMatch = settings.JobRules.StopOnFirstMatch
        };
        db.AppSettings.Add(entity);

        foreach (var rule in settings.JobRules.Rules)
        {
            rule.UserId = userId;
        }
        if (settings.JobRules.Rules.Count > 0)
        {
            db.JobRules.AddRange(settings.JobRules.Rules);
            _logger.LogInformation("Importing {Count} rules to SQL Server for user {UserId}", settings.JobRules.Rules.Count, userId);
        }

        db.SaveChanges();
        _logger.LogInformation("Import to SQL Server complete for user {UserId}", userId);
    }
}
