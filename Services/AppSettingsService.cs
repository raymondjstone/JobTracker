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
        Console.WriteLine($"[SETTINGS] EnsureSettingsLoaded - forUserId: {forUserId}, CurrentUserId: {CurrentUserId}, resolved: {userId}");

        if (userId == Guid.Empty)
        {
            Console.WriteLine($"[SETTINGS] WARNING: userId is Empty, skipping load");
            return;
        }

        lock (_lock)
        {
            if (_loadedForUser != userId)
            {
                Console.WriteLine($"[SETTINGS] Loading settings from storage for user {userId} (was loaded for {_loadedForUser})");
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
                        Console.WriteLine($"[SETTINGS] Seeded SMTP settings from appsettings.json for user {userId}");
                    }
                }

                Console.WriteLine($"[SETTINGS] Loaded {_settings.JobRules.Rules.Count} rules, EnableAutoRules: {_settings.JobRules.EnableAutoRules}");
            }
            else
            {
                Console.WriteLine($"[SETTINGS] Using cached settings for user {userId} ({_settings.JobRules.Rules.Count} rules)");
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
                Console.WriteLine("[SETTINGS] WARNING: Cannot save settings - no userId available");
                return;
            }
            Console.WriteLine($"[SETTINGS] Saving settings for user {userId}");
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
