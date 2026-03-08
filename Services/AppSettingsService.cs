using JobTracker.Models;

namespace JobTracker.Services;

public class AppSettingsService
{
    private readonly ILogger<AppSettingsService> _logger;
    private readonly IStorageBackend _storage;
    private readonly CurrentUserService _currentUser;
    private readonly IConfiguration _configuration;
    private AppSettings _settings = new();
    private Guid _loadedForUser = Guid.Empty;
    private readonly object _lock = new();

    public event Action? OnChange;

    public AppSettingsService(IStorageBackend storage, ILogger<AppSettingsService> logger, CurrentUserService currentUser, IConfiguration configuration)
    {
        _logger = logger;
        _storage = storage;
        _currentUser = currentUser;
        _configuration = configuration;
    }

    private Guid CurrentUserId => _currentUser.GetCurrentUserId();

    private void EnsureSettingsLoaded(Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        _logger.LogDebug("EnsureSettingsLoaded - forUserId: {ForUserId}, CurrentUserId: {CurrentUserId}, resolved: {UserId}", forUserId, CurrentUserId, userId);

        if (userId == Guid.Empty)
        {
            _logger.LogWarning("userId is Empty, skipping settings load");
            return;
        }

        lock (_lock)
        {
            if (_loadedForUser != userId)
            {
                _logger.LogDebug("Loading settings from storage for user {UserId} (was loaded for {PreviousUser})", userId, _loadedForUser);
                _settings = _storage.LoadSettings(userId);
                _loadedForUser = userId;

                // Seed SMTP settings from appsettings.json if user hasn't configured them yet
                if (string.IsNullOrWhiteSpace(_settings.SmtpHost))
                {
                    var configHost = _configuration["Smtp:Host"];
                    if (!string.IsNullOrWhiteSpace(configHost))
                    {
                        _settings.SmtpHost = configHost;
                        _settings.SmtpPort = int.TryParse(_configuration["Smtp:Port"], out var p) ? p : 587;
                        _settings.SmtpUsername = _configuration["Smtp:Username"] ?? "";
                        _settings.SmtpPassword = _configuration["Smtp:Password"] ?? "";
                        _settings.SmtpFromEmail = _configuration["Smtp:FromEmail"] ?? "";
                        _settings.SmtpFromName = _configuration["Smtp:FromName"] ?? "Job Tracker";
                        _storage.SaveSettings(_settings, userId);
                        _logger.LogInformation("Seeded SMTP settings from appsettings.json for user {UserId}", userId);
                    }
                }

                _logger.LogDebug("Loaded {RuleCount} rules, EnableAutoRules: {AutoRules}", _settings.JobRules.Rules.Count, _settings.JobRules.EnableAutoRules);
            }
            else
            {
                _logger.LogDebug("Using cached settings for user {UserId} ({RuleCount} rules)", userId, _settings.JobRules.Rules.Count);
            }
        }
    }

    public virtual AppSettings GetSettings(Guid? forUserId = null)
    {
        EnsureSettingsLoaded(forUserId);
        lock (_lock)
        {
            return _settings;
        }
    }

    /// <summary>
    /// Returns a copy of settings with sensitive fields decrypted for use.
    /// Use this when you need to actually use passwords/API keys.
    /// </summary>
    public AppSettings GetSettingsDecrypted(Guid? forUserId = null)
    {
        var settings = GetSettings(forUserId);
        // Return decrypted values (Decrypt handles both encrypted and plaintext)
        return new AppSettings
        {
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort,
            SmtpUsername = settings.SmtpUsername,
            SmtpPassword = SettingsEncryptionService.Decrypt(settings.SmtpPassword),
            SmtpFromEmail = settings.SmtpFromEmail,
            SmtpFromName = settings.SmtpFromName,
            ImapHost = settings.ImapHost,
            ImapPort = settings.ImapPort,
            ImapUseSsl = settings.ImapUseSsl,
            ImapUsername = settings.ImapUsername,
            ImapPassword = SettingsEncryptionService.Decrypt(settings.ImapPassword),
            ImapFolder = settings.ImapFolder,
            AIAssistant = new AIAssistantSettings
            {
                Enabled = settings.AIAssistant.Enabled,
                Provider = settings.AIAssistant.Provider,
                ApiKey = SettingsEncryptionService.Decrypt(settings.AIAssistant.ApiKey),
                Model = settings.AIAssistant.Model,
                AutoAnalyzeNewJobs = settings.AIAssistant.AutoAnalyzeNewJobs,
                AutoGenerateCoverLetter = settings.AIAssistant.AutoGenerateCoverLetter,
                ShowSkillGaps = settings.AIAssistant.ShowSkillGaps,
                ShowSimilarJobs = settings.AIAssistant.ShowSimilarJobs,
                UserSkills = settings.AIAssistant.UserSkills,
                UserExperience = settings.AIAssistant.UserExperience,
            },
            // Copy remaining non-sensitive settings by reference
            JobSiteUrls = settings.JobSiteUrls,
            JobRules = settings.JobRules,
            FilterPresets = settings.FilterPresets,
            HighlightKeywords = settings.HighlightKeywords,
            HighlightPrioritizedSkills = settings.HighlightPrioritizedSkills,
            HighlightPrioritizedSkillsInDescription = settings.HighlightPrioritizedSkillsInDescription,
            SkillsToShowOnCard = settings.SkillsToShowOnCard,
            Pipeline = settings.Pipeline,
            CoverLetterTemplates = settings.CoverLetterTemplates,
            EmailNotificationsEnabled = settings.EmailNotificationsEnabled,
            EmailOnStaleApplications = settings.EmailOnStaleApplications,
            EmailOnFollowUpDue = settings.EmailOnFollowUpDue,
            EmailCheckEnabled = settings.EmailCheckEnabled,
            EmailCheckAutoUpdateStage = settings.EmailCheckAutoUpdateStage,
            EmailCheckParseJobAlerts = settings.EmailCheckParseJobAlerts,
            AutoArchiveEnabled = settings.AutoArchiveEnabled,
            AutoArchiveDays = settings.AutoArchiveDays,
            DeleteUnsuitableAfterDays = settings.DeleteUnsuitableAfterDays,
            DeleteRejectedAfterDays = settings.DeleteRejectedAfterDays,
            DeleteGhostedAfterDays = settings.DeleteGhostedAfterDays,
            ScoringPreferences = settings.ScoringPreferences,
            DarkMode = settings.DarkMode,
            LocalCurrency = settings.LocalCurrency,
            BackupDirectory = settings.BackupDirectory,
            BackupOnStartup = settings.BackupOnStartup,
            BackupsToKeep = settings.BackupsToKeep,
            HistoryMaxEntries = settings.HistoryMaxEntries,
            CrawlPages = settings.CrawlPages,
            SearchQueries = settings.SearchQueries,
            LastViewState = settings.LastViewState,
        };
    }

    /// <summary>
    /// Encrypts sensitive fields before saving. Call this when the user updates passwords/API keys.
    /// </summary>
    public void EncryptSensitiveFields()
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_settings.SmtpPassword) && !SettingsEncryptionService.IsEncrypted(_settings.SmtpPassword))
                _settings.SmtpPassword = SettingsEncryptionService.Encrypt(_settings.SmtpPassword);

            if (!string.IsNullOrEmpty(_settings.ImapPassword) && !SettingsEncryptionService.IsEncrypted(_settings.ImapPassword))
                _settings.ImapPassword = SettingsEncryptionService.Encrypt(_settings.ImapPassword);

            if (!string.IsNullOrEmpty(_settings.AIAssistant.ApiKey) && !SettingsEncryptionService.IsEncrypted(_settings.AIAssistant.ApiKey))
                _settings.AIAssistant.ApiKey = SettingsEncryptionService.Encrypt(_settings.AIAssistant.ApiKey);
        }
    }

    public JobSiteUrls GetJobSiteUrls()
    {
        EnsureSettingsLoaded();
        lock (_lock)
        {
            return _settings.JobSiteUrls;
        }
    }

    public void UpdateJobSiteUrls(JobSiteUrls urls)
    {
        EnsureSettingsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            _settings.JobSiteUrls = urls;
            SaveSettings(userId);
            NotifyStateChanged();
            _logger.LogInformation("Job site URLs updated for user {UserId}", userId);
        }
    }

    public void UpdateJobSiteUrl(string site, string url)
    {
        EnsureSettingsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            switch (site.ToLowerInvariant())
            {
                case "linkedin":
                    _settings.JobSiteUrls.LinkedIn = url;
                    break;
                case "s1jobs":
                    _settings.JobSiteUrls.S1Jobs = url;
                    break;
                case "indeed":
                    _settings.JobSiteUrls.Indeed = url;
                    break;
                case "wttj":
                    _settings.JobSiteUrls.WTTJ = url;
                    break;
                default:
                    _logger.LogWarning("Unknown site: {Site}", site);
                    return;
            }
            SaveSettings(userId);
            NotifyStateChanged();
            _logger.LogInformation("Updated URL for {Site}: {Url}", site, url);
        }
    }

    /// <summary>
    /// Saves current settings to the storage backend.
    /// Uses the userId that was used to load the settings.
    /// Automatically encrypts sensitive fields before saving.
    /// </summary>
    public virtual void Save()
    {
        lock (_lock)
        {
            // Use _loadedForUser instead of CurrentUserId because API calls
            // don't have CurrentUserId but we still need to save to the correct user
            var userId = _loadedForUser;
            if (userId == Guid.Empty)
            {
                userId = CurrentUserId;
            }
            if (userId == Guid.Empty)
            {
                _logger.LogWarning("Cannot save settings - no userId available");
                return;
            }

            // Encrypt sensitive fields before persisting
            EncryptSensitiveFields();

            _logger.LogDebug("Saving settings for user {UserId}", userId);
            _storage.SaveSettings(_settings, userId);
        }
    }

    public List<SavedFilterPreset> GetFilterPresets()
    {
        EnsureSettingsLoaded();
        lock (_lock) { return _settings.FilterPresets; }
    }

    public void SaveFilterPreset(SavedFilterPreset preset)
    {
        EnsureSettingsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            _settings.FilterPresets.RemoveAll(p => p.Name == preset.Name);
            _settings.FilterPresets.Add(preset);
            SaveSettings(userId);
            NotifyStateChanged();
        }
    }

    public void DeleteFilterPreset(string name)
    {
        EnsureSettingsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            _settings.FilterPresets.RemoveAll(p => p.Name == name);
            SaveSettings(userId);
            NotifyStateChanged();
        }
    }

    public List<string> GetHighlightKeywords()
    {
        EnsureSettingsLoaded();
        lock (_lock) { return _settings.HighlightKeywords; }
    }

    public void UpdateHighlightKeywords(List<string> keywords)
    {
        EnsureSettingsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            _settings.HighlightKeywords = keywords;
            SaveSettings(userId);
            NotifyStateChanged();
        }
    }

    public List<CoverLetterTemplate> GetCoverLetterTemplates()
    {
        EnsureSettingsLoaded();
        lock (_lock) { return _settings.CoverLetterTemplates; }
    }

    public void SaveCoverLetterTemplate(CoverLetterTemplate template)
    {
        EnsureSettingsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            var existing = _settings.CoverLetterTemplates.FindIndex(t => t.Id == template.Id);
            if (existing >= 0)
                _settings.CoverLetterTemplates[existing] = template;
            else
                _settings.CoverLetterTemplates.Add(template);
            SaveSettings(userId);
            NotifyStateChanged();
        }
    }

    public void DeleteCoverLetterTemplate(Guid id)
    {
        EnsureSettingsLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            _settings.CoverLetterTemplates.RemoveAll(t => t.Id == id);
            SaveSettings(userId);
            NotifyStateChanged();
        }
    }

    private void SaveSettings(Guid userId)
    {
        _storage.SaveSettings(_settings, userId);
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
