using JobTracker.Models;
using System.Text.Json;

namespace JobTracker.Services;

public class JsonStorageBackend : IStorageBackend
{
    private readonly string _dataDirectory;
    private readonly string _jobsFilePath;
    private readonly string _historyFilePath;
    private readonly string _settingsFilePath;
    private readonly string _usersFilePath;
    private readonly ILogger<JsonStorageBackend> _logger;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public JsonStorageBackend(string dataDirectory, ILogger<JsonStorageBackend> logger)
    {
        _dataDirectory = dataDirectory;
        _logger = logger;
        _jobsFilePath = Path.Combine(dataDirectory, "jobs.json");
        _historyFilePath = Path.Combine(dataDirectory, "history.json");
        _settingsFilePath = Path.Combine(dataDirectory, "settings.json");
        _usersFilePath = Path.Combine(dataDirectory, "users.json");

        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }
    }

    // User operations
    public User? GetUserById(Guid id)
    {
        return GetAllUsers().FirstOrDefault(u => u.Id == id);
    }

    public User? GetUserByEmail(string email)
    {
        return GetAllUsers().FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }

    public User? GetUserByResetToken(string token)
    {
        return GetAllUsers().FirstOrDefault(u => u.PasswordResetToken == token);
    }

    public List<User> GetAllUsers()
    {
        try
        {
            if (File.Exists(_usersFilePath))
            {
                var json = File.ReadAllText(_usersFilePath);
                var users = JsonSerializer.Deserialize<List<User>>(json, ReadOptions);
                return users ?? new List<User>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading users from file");
        }
        return new List<User>();
    }

    public void SaveUser(User user)
    {
        var users = GetAllUsers();
        var index = users.FindIndex(u => u.Id == user.Id);
        if (index >= 0)
            users[index] = user;
        else
            users.Add(user);
        SaveUsers(users);
    }

    public void AddUser(User user)
    {
        var users = GetAllUsers();
        users.Add(user);
        SaveUsers(users);
        _logger.LogInformation("Added user: {Email}", user.Email);
    }

    public void DeleteUser(Guid id)
    {
        var users = GetAllUsers();
        users.RemoveAll(u => u.Id == id);
        SaveUsers(users);
    }

    private void SaveUsers(List<User> users)
    {
        try
        {
            var json = JsonSerializer.Serialize(users, WriteOptions);
            File.WriteAllText(_usersFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving users to file");
        }
    }

    public List<JobListing> LoadJobs(Guid userId)
    {
        try
        {
            if (File.Exists(_jobsFilePath))
            {
                var json = File.ReadAllText(_jobsFilePath);
                var jobs = JsonSerializer.Deserialize<List<JobListing>>(json, ReadOptions);
                if (jobs != null)
                {
                    // Filter by user, but also include jobs with empty UserId for migration
                    var userJobs = jobs.Where(j => j.UserId == userId || j.UserId == Guid.Empty).ToList();
                    _logger.LogInformation("Loaded {Count} jobs from {Path} for user {UserId}", userJobs.Count, _jobsFilePath, userId);
                    return userJobs;
                }
            }
            else
            {
                _logger.LogInformation("No existing data file found at {Path}", _jobsFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading jobs from file");
        }
        return new List<JobListing>();
    }

    public void SaveJobs(List<JobListing> jobs, Guid userId)
    {
        try
        {
            // Load all jobs, remove this user's jobs, then add the new list
            var allJobs = new List<JobListing>();
            if (File.Exists(_jobsFilePath))
            {
                var existingJson = File.ReadAllText(_jobsFilePath);
                var existingJobs = JsonSerializer.Deserialize<List<JobListing>>(existingJson, ReadOptions);
                if (existingJobs != null)
                {
                    allJobs = existingJobs.Where(j => j.UserId != userId).ToList();
                }
            }
            allJobs.AddRange(jobs);

            var json = JsonSerializer.Serialize(allJobs, WriteOptions);
            File.WriteAllText(_jobsFilePath, json);
            _logger.LogDebug("Saved {Count} jobs to {Path} for user {UserId}", jobs.Count, _jobsFilePath, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving jobs to file");
        }
    }

    // For JSON, targeted operations do a read-modify-write of the full file.
    // Less efficient than SQL targeted ops, but still correct and fast enough for file I/O.

    public void SaveJob(JobListing job)
    {
        // Load all jobs, update this one
        var allJobs = new List<JobListing>();
        if (File.Exists(_jobsFilePath))
        {
            var existingJson = File.ReadAllText(_jobsFilePath);
            allJobs = JsonSerializer.Deserialize<List<JobListing>>(existingJson, ReadOptions) ?? new List<JobListing>();
        }

        var index = allJobs.FindIndex(j => j.Id == job.Id);
        if (index >= 0)
            allJobs[index] = job;

        var json = JsonSerializer.Serialize(allJobs, WriteOptions);
        File.WriteAllText(_jobsFilePath, json);
    }

    public void AddJob(JobListing job)
    {
        var allJobs = new List<JobListing>();
        if (File.Exists(_jobsFilePath))
        {
            var existingJson = File.ReadAllText(_jobsFilePath);
            allJobs = JsonSerializer.Deserialize<List<JobListing>>(existingJson, ReadOptions) ?? new List<JobListing>();
        }
        allJobs.Add(job);

        var json = JsonSerializer.Serialize(allJobs, WriteOptions);
        File.WriteAllText(_jobsFilePath, json);
    }

    public void DeleteJob(Guid id)
    {
        var allJobs = new List<JobListing>();
        if (File.Exists(_jobsFilePath))
        {
            var existingJson = File.ReadAllText(_jobsFilePath);
            allJobs = JsonSerializer.Deserialize<List<JobListing>>(existingJson, ReadOptions) ?? new List<JobListing>();
        }
        allJobs.RemoveAll(j => j.Id == id);

        var json = JsonSerializer.Serialize(allJobs, WriteOptions);
        File.WriteAllText(_jobsFilePath, json);
    }

    public void DeleteAllJobs(Guid userId)
    {
        var allJobs = new List<JobListing>();
        if (File.Exists(_jobsFilePath))
        {
            var existingJson = File.ReadAllText(_jobsFilePath);
            allJobs = JsonSerializer.Deserialize<List<JobListing>>(existingJson, ReadOptions) ?? new List<JobListing>();
        }
        allJobs.RemoveAll(j => j.UserId == userId);

        var json = JsonSerializer.Serialize(allJobs, WriteOptions);
        File.WriteAllText(_jobsFilePath, json);
    }

    public void AddHistoryEntry(JobHistoryEntry entry)
    {
        var allHistory = LoadAllHistory();
        allHistory.Insert(0, entry);

        // Enforce per-user cap
        var userHistory = allHistory.Where(h => h.UserId == entry.UserId).Take(10000).ToList();
        var otherHistory = allHistory.Where(h => h.UserId != entry.UserId).ToList();
        allHistory = otherHistory.Concat(userHistory).ToList();

        SaveAllHistory(allHistory);
    }

    public void DeleteAllHistory(Guid userId)
    {
        var allHistory = LoadAllHistory();
        allHistory.RemoveAll(h => h.UserId == userId);
        SaveAllHistory(allHistory);
    }

    public List<JobHistoryEntry> LoadHistory(Guid userId)
    {
        var allHistory = LoadAllHistory();
        return allHistory
            .Where(h => h.UserId == userId || h.UserId == Guid.Empty)
            .OrderByDescending(h => h.Timestamp)
            .ToList();
    }

    private List<JobHistoryEntry> LoadAllHistory()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = File.ReadAllText(_historyFilePath);
                var history = JsonSerializer.Deserialize<List<JobHistoryEntry>>(json, ReadOptions);
                if (history != null)
                {
                    return history;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading history from file");
        }
        return new List<JobHistoryEntry>();
    }

    public void SaveHistory(List<JobHistoryEntry> history, Guid userId)
    {
        var allHistory = LoadAllHistory();
        allHistory.RemoveAll(h => h.UserId == userId);
        allHistory.AddRange(history);
        SaveAllHistory(allHistory);
    }

    private void SaveAllHistory(List<JobHistoryEntry> history)
    {
        try
        {
            var directory = Path.GetDirectoryName(_historyFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(history, WriteOptions);
            File.WriteAllText(_historyFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving history to file");
        }
    }

    public AppSettings LoadSettings(Guid userId)
    {
        try
        {
            // For JSON backend, we use a per-user settings file
            var userSettingsPath = Path.Combine(_dataDirectory, $"settings_{userId}.json");
            if (File.Exists(userSettingsPath))
            {
                var json = File.ReadAllText(userSettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions);
                if (settings != null)
                {
                    _logger.LogInformation("Settings loaded from {Path} for user {UserId}", userSettingsPath, userId);
                    return settings;
                }
            }
            // Fallback to old settings file for migration
            else if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions);
                if (settings != null)
                {
                    _logger.LogInformation("Settings loaded from legacy {Path}", _settingsFilePath);
                    return settings;
                }
            }
            else
            {
                _logger.LogInformation("No settings file found for user {UserId}, using defaults", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings from file");
        }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings, Guid userId)
    {
        try
        {
            var userSettingsPath = Path.Combine(_dataDirectory, $"settings_{userId}.json");
            var json = JsonSerializer.Serialize(settings, WriteOptions);
            File.WriteAllText(userSettingsPath, json);
            _logger.LogDebug("Settings saved to {Path} for user {UserId}", userSettingsPath, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings to file");
        }
    }

    /// <summary>
    /// Migrate existing data to the specified user
    /// </summary>
    public void MigrateExistingDataToUser(Guid userId)
    {
        // Migrate jobs
        if (File.Exists(_jobsFilePath))
        {
            var json = File.ReadAllText(_jobsFilePath);
            var jobs = JsonSerializer.Deserialize<List<JobListing>>(json, ReadOptions);
            if (jobs != null)
            {
                var migratedCount = 0;
                foreach (var job in jobs.Where(j => j.UserId == Guid.Empty))
                {
                    job.UserId = userId;
                    migratedCount++;
                }
                if (migratedCount > 0)
                {
                    File.WriteAllText(_jobsFilePath, JsonSerializer.Serialize(jobs, WriteOptions));
                    _logger.LogInformation("Migrated {Count} jobs to user {UserId}", migratedCount, userId);
                }
            }
        }

        // Migrate history
        if (File.Exists(_historyFilePath))
        {
            var json = File.ReadAllText(_historyFilePath);
            var history = JsonSerializer.Deserialize<List<JobHistoryEntry>>(json, ReadOptions);
            if (history != null)
            {
                var migratedCount = 0;
                foreach (var entry in history.Where(h => h.UserId == Guid.Empty))
                {
                    entry.UserId = userId;
                    migratedCount++;
                }
                if (migratedCount > 0)
                {
                    File.WriteAllText(_historyFilePath, JsonSerializer.Serialize(history, WriteOptions));
                    _logger.LogInformation("Migrated {Count} history entries to user {UserId}", migratedCount, userId);
                }
            }
        }

        // Migrate settings - copy old settings file to user-specific file
        if (File.Exists(_settingsFilePath))
        {
            var userSettingsPath = Path.Combine(_dataDirectory, $"settings_{userId}.json");
            if (!File.Exists(userSettingsPath))
            {
                File.Copy(_settingsFilePath, userSettingsPath);
                _logger.LogInformation("Migrated settings to user {UserId}", userId);
            }
        }
    }

    /// <summary>
    /// Whether JSON data files exist (used for import detection)
    /// </summary>
    public bool HasExistingData()
    {
        return File.Exists(_jobsFilePath) || File.Exists(_historyFilePath) || File.Exists(_settingsFilePath);
    }
}
