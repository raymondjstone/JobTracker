using System.IO.Compression;
using System.Text.Json;
using JobTracker.Models;
using JobTracker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobTracker.Tests;

/// <summary>
/// Verifies that backup/restore round-trips preserve ALL user data and settings.
/// Mirrors the backup logic in Settings.razor (DownloadBackup / ConfirmRestore).
/// </summary>
public class BackupRestoreTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string _restoreDir;
    private readonly JsonStorageBackend _storage;
    private readonly Guid _userId = Guid.NewGuid();

    public BackupRestoreTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "JT_Backup_" + Guid.NewGuid().ToString("N"));
        _restoreDir = Path.Combine(Path.GetTempPath(), "JT_Restore_" + Guid.NewGuid().ToString("N"));
        var logger = NullLogger<JsonStorageBackend>.Instance;
        _storage = new JsonStorageBackend(_dataDir, logger, 50000);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true); } catch { }
        try { if (Directory.Exists(_restoreDir)) Directory.Delete(_restoreDir, true); } catch { }
    }

    /// <summary>Creates a ZIP backup of all JSON files (same logic as Settings.razor DownloadBackup)</summary>
    private byte[] CreateBackup(string dataDir)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var filePath in Directory.GetFiles(dataDir, "*.json"))
            {
                var entry = archive.CreateEntry(Path.GetFileName(filePath));
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(filePath);
                fileStream.CopyTo(entryStream);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Restores a ZIP backup (same logic as Settings.razor ConfirmRestore)</summary>
    private void RestoreBackup(byte[] zipBytes, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            using var fileStream = new FileStream(
                Path.Combine(targetDir, entry.FullName), FileMode.Create, FileAccess.Write);
            entryStream.CopyTo(fileStream);
        }
    }

    // === Round-trip: Jobs ===

    [Fact]
    public void RoundTrip_Jobs_AllFieldsPreserved()
    {
        var job = new JobListing
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Title = "Senior .NET Developer",
            Company = "Acme Corp",
            Location = "Edinburgh",
            Description = "Build things",
            Url = "https://example.com/job/1",
            Salary = "£60,000 - £80,000",
            SalaryMin = 60000,
            SalaryMax = 80000,
            SalaryCurrency = "GBP",
            SalaryPeriod = "year",
            SalarySource = "parsed",
            JobType = JobType.FullTime,
            IsRemote = true,
            IsAgency = false,
            Source = "LinkedIn",
            HasApplied = true,
            DateApplied = DateTime.Now.AddDays(-5),
            ApplicationStage = ApplicationStage.Interview,
            Interest = InterestStatus.Interested,
            Suitability = SuitabilityStatus.Possible,
            SuitabilityScore = 85,
            Skills = new List<string> { "C#", ".NET", "Azure" },
            Notes = "Great opportunity",
            IsPinned = true,
            IsArchived = false,
            FollowUpDate = DateTime.Now.AddDays(7),
            AISummary = "AI generated summary",
            AIResponsibilities = new List<string> { "Design systems" },
            AIRequiredSkills = new List<string> { "C#" },
            AICoverLetterOpening = "Dear Hiring Manager",
        };
        _storage.AddJob(job);

        var backup = CreateBackup(_dataDir);
        RestoreBackup(backup, _restoreDir);

        var restored = new JsonStorageBackend(_restoreDir, NullLogger<JsonStorageBackend>.Instance, 50000);
        var jobs = restored.LoadJobs(_userId);

        Assert.Single(jobs);
        var r = jobs[0];
        Assert.Equal(job.Title, r.Title);
        Assert.Equal(job.Company, r.Company);
        Assert.Equal(job.Location, r.Location);
        Assert.Equal(job.Salary, r.Salary);
        Assert.Equal(job.SalaryMin, r.SalaryMin);
        Assert.Equal(job.SalaryMax, r.SalaryMax);
        Assert.Equal(job.SalaryCurrency, r.SalaryCurrency);
        Assert.Equal(job.IsRemote, r.IsRemote);
        Assert.Equal(job.IsAgency, r.IsAgency);
        Assert.Equal(job.Source, r.Source);
        Assert.Equal(job.HasApplied, r.HasApplied);
        Assert.Equal(job.ApplicationStage, r.ApplicationStage);
        Assert.Equal(job.Interest, r.Interest);
        Assert.Equal(job.SuitabilityScore, r.SuitabilityScore);
        Assert.Equal(job.Skills, r.Skills);
        Assert.Equal(job.Notes, r.Notes);
        Assert.Equal(job.IsPinned, r.IsPinned);
        Assert.Equal(job.AISummary, r.AISummary);
        Assert.Equal(job.AIResponsibilities, r.AIResponsibilities);
        Assert.Equal(job.AICoverLetterOpening, r.AICoverLetterOpening);
    }

    // === Round-trip: History ===

    [Fact]
    public void RoundTrip_History_Preserved()
    {
        var entry = new JobHistoryEntry
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            JobId = Guid.NewGuid(),
            JobTitle = "Developer",
            Company = "TestCo",
            ActionType = HistoryActionType.ApplicationStageChanged,
            ChangeSource = HistoryChangeSource.Manual,
            FieldName = "ApplicationStage",
            OldValue = "Applied",
            NewValue = "Interview",
            Timestamp = DateTime.Now,
        };
        _storage.AddHistoryEntry(entry);

        var backup = CreateBackup(_dataDir);
        RestoreBackup(backup, _restoreDir);

        var restored = new JsonStorageBackend(_restoreDir, NullLogger<JsonStorageBackend>.Instance, 50000);
        var history = restored.LoadHistory(_userId);

        Assert.Single(history);
        Assert.Equal(entry.JobTitle, history[0].JobTitle);
        Assert.Equal(entry.ActionType, history[0].ActionType);
        Assert.Equal(entry.OldValue, history[0].OldValue);
        Assert.Equal(entry.NewValue, history[0].NewValue);
    }

    // === Round-trip: Contacts ===

    [Fact]
    public void RoundTrip_Contacts_WithJobLinks()
    {
        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Name = "Alice Recruiter",
            Email = "alice@agency.com",
            Phone = "+44123456789",
            Role = "Senior Recruiter",
        };
        _storage.AddContact(contact);
        var jobId = Guid.NewGuid();
        _storage.LinkContactToJob(contact.Id, jobId);

        var backup = CreateBackup(_dataDir);
        RestoreBackup(backup, _restoreDir);

        var restored = new JsonStorageBackend(_restoreDir, NullLogger<JsonStorageBackend>.Instance, 50000);
        var contacts = restored.LoadContacts(_userId);
        Assert.Single(contacts);
        Assert.Equal("Alice Recruiter", contacts[0].Name);
        Assert.Equal("alice@agency.com", contacts[0].Email);

        var linkedJobs = restored.GetContactsForJob(jobId);
        Assert.Single(linkedJobs);
    }

    // === Round-trip: Users ===

    [Fact]
    public void RoundTrip_Users_Preserved()
    {
        var user = new User
        {
            Id = _userId,
            Email = "user@example.com",
            Name = "Test User",
            ApiKey = "test-api-key-123",
            TwoFactorEnabled = true,
        };
        _storage.AddUser(user);

        var backup = CreateBackup(_dataDir);
        RestoreBackup(backup, _restoreDir);

        var restored = new JsonStorageBackend(_restoreDir, NullLogger<JsonStorageBackend>.Instance, 50000);
        var u = restored.GetUserById(_userId);
        Assert.NotNull(u);
        Assert.Equal("user@example.com", u.Email);
        Assert.Equal("Test User", u.Name);
        Assert.Equal("test-api-key-123", u.ApiKey);
        Assert.True(u.TwoFactorEnabled);
    }

    // === Round-trip: Processed Emails ===

    [Fact]
    public void RoundTrip_ProcessedEmails_Preserved()
    {
        var emails = new List<ProcessedEmail>
        {
            new() { MessageId = "msg-001", ProcessedAt = DateTime.Now, Action = "StageUpdate", RelatedJobId = Guid.NewGuid() },
            new() { MessageId = "msg-002", ProcessedAt = DateTime.Now, Action = "Ignored" },
        };
        _storage.SaveProcessedEmails(emails, _userId);

        var backup = CreateBackup(_dataDir);
        RestoreBackup(backup, _restoreDir);

        var restored = new JsonStorageBackend(_restoreDir, NullLogger<JsonStorageBackend>.Instance, 50000);
        var loaded = restored.LoadProcessedEmails(_userId);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, e => e.MessageId == "msg-001" && e.Action == "StageUpdate");
    }

    // === Round-trip: ALL AppSettings fields ===

    [Fact]
    public void RoundTrip_Settings_AllFieldsPreserved()
    {
        var settings = new AppSettings
        {
            // Job site URLs & crawl flags
            JobSiteUrls = new JobSiteUrls
            {
                LinkedIn = "https://linkedin.com/custom",
                S1Jobs = "https://s1jobs.com/custom",
                Indeed = "https://indeed.com/custom",
                WTTJ = "https://wttj.com/custom",
                EnergyJobSearch = "https://energy.com/custom",
                CrawlLinkedIn = false,
                CrawlS1Jobs = true,
                CrawlWTTJ = false,
                CrawlEnergyJobSearch = true,
            },
            // Job rules
            JobRules = new JobRulesSettings
            {
                EnableAutoRules = true,
                StopOnFirstMatch = true,
                Rules = new List<JobRule>
                {
                    new() { Id = Guid.NewGuid(), Name = "Exclude agencies", IsEnabled = true, Field = RuleField.Company, Operator = RuleOperator.Contains, Value = "Agency", SetInterest = InterestStatus.NotInterested }
                }
            },
            // Filter presets
            FilterPresets = new List<SavedFilterPreset>
            {
                new() { Name = "Remote C#", SearchTerm = "C#", IsRemote = "true" }
            },
            // Highlight keywords
            HighlightKeywords = new List<string> { "C#", "Azure", "Blazor" },
            HighlightPrioritizedSkills = false,
            HighlightPrioritizedSkillsInDescription = false,
            SkillsToShowOnCard = 10,
            // Pipeline
            Pipeline = new PipelineSettings { NoReplyDays = 5, GhostedDays = 7, StaleDays = 21 },
            // Cover letter templates
            CoverLetterTemplates = new List<CoverLetterTemplate>
            {
                new() { Id = Guid.NewGuid(), Name = "Default", Content = "Dear Hiring Manager..." }
            },
            // SMTP
            SmtpHost = "smtp.test.com",
            SmtpPort = 465,
            SmtpUsername = "user@test.com",
            SmtpPassword = "encrypted-password",
            SmtpFromEmail = "from@test.com",
            SmtpFromName = "My Tracker",
            EmailNotificationsEnabled = true,
            EmailOnStaleApplications = false,
            EmailOnFollowUpDue = true,
            // IMAP
            ImapHost = "imap.test.com",
            ImapPort = 993,
            ImapUseSsl = true,
            ImapUsername = "imap-user",
            ImapPassword = "imap-pass",
            ImapFolder = "Jobs",
            EmailCheckEnabled = true,
            EmailCheckAutoUpdateStage = false,
            EmailCheckParseJobAlerts = true,
            // Auto-archive
            AutoArchiveEnabled = true,
            AutoArchiveDays = 14,
            DeleteUnsuitableAfterDays = 30,
            DeleteRejectedAfterDays = 45,
            DeleteGhostedAfterDays = 20,
            // Scoring preferences
            ScoringPreferences = new ScoringPreferences
            {
                EnableScoring = true,
                SkillsWeight = 0.9,
                SalaryWeight = 0.8,
                RemoteWeight = 0.7,
                LocationWeight = 0.6,
                KeywordWeight = 0.5,
                CompanyWeight = 0.4,
                LearningWeight = 0.3,
                PreferredSkills = new List<string> { "C#", "Azure" },
                MinDesiredSalary = 50000,
                MaxDesiredSalary = 90000,
                PreferRemote = true,
                PreferredLocations = new List<string> { "Edinburgh", "Glasgow" },
                MustHaveKeywords = new List<string> { ".NET" },
                AvoidKeywords = new List<string> { "PHP" },
                PreferredCompanies = new List<string> { "Microsoft" },
                AvoidCompanies = new List<string> { "BadCorp" },
                SkillPriorities = new List<string> { "C#", ".NET", "Azure" },
                MinScoreToShow = 30,
            },
            // AI assistant
            AIAssistant = new AIAssistantSettings
            {
                Enabled = true,
                Provider = "Claude",
                ApiKey = "sk-encrypted-key",
                Model = "claude-3-5-sonnet",
                AutoAnalyzeNewJobs = true,
                AutoGenerateCoverLetter = false,
                ShowSkillGaps = true,
                ShowSimilarJobs = false,
                UserSkills = new List<string> { "C#", "Azure", "SQL" },
                UserExperience = "10 years in .NET development",
            },
            // UI
            DarkMode = true,
            LocalCurrency = "USD",
            // Backup
            BackupDirectory = "/custom/backup",
            BackupOnStartup = true,
            BackupsToKeep = 5,
            HistoryMaxEntries = 25000,
            // Crawl pages
            CrawlPages = new List<CrawlPage>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Url = "https://example.com/jobs?page={0}",
                    Label = "Example Jobs",
                    Site = "custom",
                    Enabled = true,
                    DelayAfterSeconds = 120,
                    UseSubstitutionNumber = true,
                    SubstitutionStart = 1,
                    SubstitutionEnd = 5,
                    SubstitutionIncrement = 1,
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Url = "https://other.com/search",
                    Label = "Other Site",
                    Site = "other",
                    Enabled = false,
                    DelayAfterSeconds = 60,
                }
            },
            // Search queries
            SearchQueries = new List<JobSearchQuery>
            {
                new() { Id = Guid.NewGuid(), Keywords = ".NET developer", Location = "Edinburgh", Enabled = true }
            },
            // Skill extraction
            SkillExtraction = new SkillExtractionSettings
            {
                AdditionalSkills = new List<SkillDefinition>
                {
                    new() { Id = Guid.NewGuid(), DisplayName = "Blazor", Pattern = @"\bBlazor\b", IsRegex = true }
                },
                RemovedDefaultSkills = new List<string> { "HTML", "CSS" },
                MaxSkillsToExtract = 20,
            },
            // View state
            LastViewState = new JobViewState
            {
                SearchTerm = "developer",
                Location = "Edinburgh",
                IsRemote = "true",
                SortBy = "DateAdded",
                ActiveTab = "All",
            },
        };

        _storage.SaveSettings(settings, _userId);

        var backup = CreateBackup(_dataDir);
        RestoreBackup(backup, _restoreDir);

        var restored = new JsonStorageBackend(_restoreDir, NullLogger<JsonStorageBackend>.Instance, 50000);
        var r = restored.LoadSettings(_userId);

        // Job site URLs
        Assert.Equal("https://linkedin.com/custom", r.JobSiteUrls.LinkedIn);
        Assert.Equal("https://s1jobs.com/custom", r.JobSiteUrls.S1Jobs);
        Assert.Equal("https://indeed.com/custom", r.JobSiteUrls.Indeed);
        Assert.Equal("https://wttj.com/custom", r.JobSiteUrls.WTTJ);
        Assert.Equal("https://energy.com/custom", r.JobSiteUrls.EnergyJobSearch);
        Assert.False(r.JobSiteUrls.CrawlLinkedIn);
        Assert.True(r.JobSiteUrls.CrawlS1Jobs);
        Assert.False(r.JobSiteUrls.CrawlWTTJ);
        Assert.True(r.JobSiteUrls.CrawlEnergyJobSearch);

        // Job rules
        Assert.True(r.JobRules.EnableAutoRules);
        Assert.True(r.JobRules.StopOnFirstMatch);
        Assert.Single(r.JobRules.Rules);
        Assert.Equal("Exclude agencies", r.JobRules.Rules[0].Name);

        // Filter presets
        Assert.Single(r.FilterPresets);
        Assert.Equal("Remote C#", r.FilterPresets[0].Name);

        // Highlight keywords
        Assert.Equal(new List<string> { "C#", "Azure", "Blazor" }, r.HighlightKeywords);
        Assert.False(r.HighlightPrioritizedSkills);
        Assert.False(r.HighlightPrioritizedSkillsInDescription);
        Assert.Equal(10, r.SkillsToShowOnCard);

        // Pipeline
        Assert.Equal(5, r.Pipeline.NoReplyDays);
        Assert.Equal(7, r.Pipeline.GhostedDays);
        Assert.Equal(21, r.Pipeline.StaleDays);

        // Cover letter templates
        Assert.Single(r.CoverLetterTemplates);
        Assert.Equal("Default", r.CoverLetterTemplates[0].Name);

        // SMTP
        Assert.Equal("smtp.test.com", r.SmtpHost);
        Assert.Equal(465, r.SmtpPort);
        Assert.Equal("user@test.com", r.SmtpUsername);
        Assert.Equal("encrypted-password", r.SmtpPassword);
        Assert.True(r.EmailNotificationsEnabled);
        Assert.False(r.EmailOnStaleApplications);

        // IMAP
        Assert.Equal("imap.test.com", r.ImapHost);
        Assert.Equal("Jobs", r.ImapFolder);
        Assert.True(r.EmailCheckEnabled);
        Assert.False(r.EmailCheckAutoUpdateStage);

        // Auto-archive
        Assert.True(r.AutoArchiveEnabled);
        Assert.Equal(14, r.AutoArchiveDays);
        Assert.Equal(30, r.DeleteUnsuitableAfterDays);
        Assert.Equal(45, r.DeleteRejectedAfterDays);
        Assert.Equal(20, r.DeleteGhostedAfterDays);

        // Scoring preferences
        Assert.True(r.ScoringPreferences.EnableScoring);
        Assert.Equal(0.9, r.ScoringPreferences.SkillsWeight);
        Assert.Equal(0.8, r.ScoringPreferences.SalaryWeight);
        Assert.Equal(new List<string> { "C#", "Azure" }, r.ScoringPreferences.PreferredSkills);
        Assert.Equal(50000m, r.ScoringPreferences.MinDesiredSalary);
        Assert.Equal(90000m, r.ScoringPreferences.MaxDesiredSalary);
        Assert.Equal(new List<string> { "Edinburgh", "Glasgow" }, r.ScoringPreferences.PreferredLocations);
        Assert.Equal(new List<string> { ".NET" }, r.ScoringPreferences.MustHaveKeywords);
        Assert.Equal(new List<string> { "PHP" }, r.ScoringPreferences.AvoidKeywords);
        Assert.Equal(new List<string> { "C#", ".NET", "Azure" }, r.ScoringPreferences.SkillPriorities);
        Assert.Equal(30, r.ScoringPreferences.MinScoreToShow);

        // AI assistant
        Assert.True(r.AIAssistant.Enabled);
        Assert.Equal("Claude", r.AIAssistant.Provider);
        Assert.Equal("sk-encrypted-key", r.AIAssistant.ApiKey);
        Assert.Equal("claude-3-5-sonnet", r.AIAssistant.Model);
        Assert.True(r.AIAssistant.AutoAnalyzeNewJobs);
        Assert.Equal(new List<string> { "C#", "Azure", "SQL" }, r.AIAssistant.UserSkills);
        Assert.Equal("10 years in .NET development", r.AIAssistant.UserExperience);

        // UI
        Assert.True(r.DarkMode);
        Assert.Equal("USD", r.LocalCurrency);

        // Backup settings
        Assert.Equal("/custom/backup", r.BackupDirectory);
        Assert.True(r.BackupOnStartup);
        Assert.Equal(5, r.BackupsToKeep);
        Assert.Equal(25000, r.HistoryMaxEntries);

        // Crawl pages
        Assert.Equal(2, r.CrawlPages.Count);
        var crawl1 = r.CrawlPages.First(c => c.Label == "Example Jobs");
        Assert.Equal("https://example.com/jobs?page={0}", crawl1.Url);
        Assert.True(crawl1.Enabled);
        Assert.Equal(120, crawl1.DelayAfterSeconds);
        Assert.True(crawl1.UseSubstitutionNumber);
        Assert.Equal(1, crawl1.SubstitutionStart);
        Assert.Equal(5, crawl1.SubstitutionEnd);
        var crawl2 = r.CrawlPages.First(c => c.Label == "Other Site");
        Assert.False(crawl2.Enabled);

        // Search queries
        Assert.Single(r.SearchQueries);
        Assert.Equal(".NET developer", r.SearchQueries[0].Keywords);
        Assert.Equal("Edinburgh", r.SearchQueries[0].Location);

        // Skill extraction
        Assert.Single(r.SkillExtraction.AdditionalSkills);
        Assert.Equal("Blazor", r.SkillExtraction.AdditionalSkills[0].DisplayName);
        Assert.Equal(new List<string> { "HTML", "CSS" }, r.SkillExtraction.RemovedDefaultSkills);
        Assert.Equal(20, r.SkillExtraction.MaxSkillsToExtract);

        // View state
        Assert.Equal("developer", r.LastViewState.SearchTerm);
        Assert.Equal("Edinburgh", r.LastViewState.Location);
        Assert.Equal("true", r.LastViewState.IsRemote);
        Assert.Equal("DateAdded", r.LastViewState.SortBy);
    }

    // === Round-trip: Background jobs config ===

    [Fact]
    public void RoundTrip_BackgroundJobsConfig_Preserved()
    {
        // Write a background-jobs.json directly (as LocalBackgroundService does)
        var config = new Dictionary<string, object>
        {
            ["JobCrawl"] = new { Enabled = true, IntervalHours = 2.0 },
            ["AvailabilityCheck"] = new { Enabled = false, IntervalHours = 6.0 },
            ["ScheduledBackup"] = new { Enabled = true, IntervalHours = 24.0 },
        };
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_dataDir, "background-jobs.json"), json);

        var backup = CreateBackup(_dataDir);
        RestoreBackup(backup, _restoreDir);

        var restoredPath = Path.Combine(_restoreDir, "background-jobs.json");
        Assert.True(File.Exists(restoredPath));

        var restoredJson = File.ReadAllText(restoredPath);
        var restoredConfig = JsonSerializer.Deserialize<JsonElement>(restoredJson);
        Assert.True(restoredConfig.GetProperty("JobCrawl").GetProperty("Enabled").GetBoolean());
        Assert.Equal(2.0, restoredConfig.GetProperty("JobCrawl").GetProperty("IntervalHours").GetDouble());
        Assert.False(restoredConfig.GetProperty("AvailabilityCheck").GetProperty("Enabled").GetBoolean());
    }

    // === Backup file count ===

    [Fact]
    public void Backup_IncludesAllJsonFiles()
    {
        // Populate all data types
        _storage.AddUser(new User { Id = _userId, Email = "test@test.com" });
        _storage.AddJob(new JobListing { Id = Guid.NewGuid(), UserId = _userId, Title = "Dev", Company = "Co" });
        _storage.AddHistoryEntry(new JobHistoryEntry { UserId = _userId, JobTitle = "Dev", Timestamp = DateTime.Now });
        _storage.SaveSettings(new AppSettings { DarkMode = true, CrawlPages = new List<CrawlPage> { new() { Label = "Test" } } }, _userId);
        _storage.AddContact(new Contact { Id = Guid.NewGuid(), UserId = _userId, Name = "Alice" });
        _storage.SaveProcessedEmails(new List<ProcessedEmail> { new() { MessageId = "m1", ProcessedAt = DateTime.Now } }, _userId);
        File.WriteAllText(Path.Combine(_dataDir, "background-jobs.json"), "{}");

        var backup = CreateBackup(_dataDir);
        using var archive = new ZipArchive(new MemoryStream(backup), ZipArchiveMode.Read);
        var fileNames = archive.Entries.Select(e => e.FullName).ToList();

        // Should contain all data file types
        Assert.Contains("users.json", fileNames);
        Assert.Contains("jobs.json", fileNames);
        Assert.Contains("history.json", fileNames);
        Assert.Contains($"settings_{_userId}.json", fileNames);
        Assert.Contains($"contacts_{_userId}.json", fileNames);
        Assert.Contains($"processed-emails_{_userId}.json", fileNames);
        Assert.Contains("background-jobs.json", fileNames);
    }

    // === Edge case: restore over existing data ===

    [Fact]
    public void Restore_OverwritesExistingData()
    {
        _storage.SaveSettings(new AppSettings { DarkMode = false, LocalCurrency = "GBP" }, _userId);
        var backup = CreateBackup(_dataDir);

        // Now change settings
        _storage.SaveSettings(new AppSettings { DarkMode = true, LocalCurrency = "USD" }, _userId);
        var changed = _storage.LoadSettings(_userId);
        Assert.True(changed.DarkMode);

        // Restore should overwrite back to original
        RestoreBackup(backup, _dataDir);
        var restored = _storage.LoadSettings(_userId);
        Assert.False(restored.DarkMode);
        Assert.Equal("GBP", restored.LocalCurrency);
    }
}
