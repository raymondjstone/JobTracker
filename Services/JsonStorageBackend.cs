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
    private readonly int _historyMax;

    // File-level locks to prevent concurrent read-modify-write corruption
    private readonly object _jobsLock = new();
    private readonly object _usersLock = new();
    private readonly object _historyLock = new();
    private readonly object _settingsLock = new();
    private readonly object _contactsLock = new();

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public JsonStorageBackend(string dataDirectory, ILogger<JsonStorageBackend> logger, int historyMax)
    {
        _dataDirectory = dataDirectory;
        _logger = logger;
        _historyMax = historyMax <= 0 ? 50000 : historyMax;
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
        lock (_usersLock)
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
    }

    public void SaveUser(User user)
    {
        lock (_usersLock)
        {
            var users = LoadUsersUnsafe();
            var index = users.FindIndex(u => u.Id == user.Id);
            if (index >= 0)
                users[index] = user;
            else
                users.Add(user);
            WriteUsersUnsafe(users);
        }
    }

    public void AddUser(User user)
    {
        lock (_usersLock)
        {
            var users = LoadUsersUnsafe();
            users.Add(user);
            WriteUsersUnsafe(users);
            _logger.LogInformation("Added user: {Email}", user.Email);
        }
    }

    public void DeleteUser(Guid id)
    {
        lock (_usersLock)
        {
            var users = LoadUsersUnsafe();
            users.RemoveAll(u => u.Id == id);
            WriteUsersUnsafe(users);
        }
    }

    // Internal helpers that assume _usersLock is already held
    private List<User> LoadUsersUnsafe()
    {
        try
        {
            if (File.Exists(_usersFilePath))
            {
                var json = File.ReadAllText(_usersFilePath);
                return JsonSerializer.Deserialize<List<User>>(json, ReadOptions) ?? new List<User>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading users from file");
        }
        return new List<User>();
    }

    private void WriteUsersUnsafe(List<User> users)
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
        lock (_jobsLock)
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
    }

    public void SaveJobs(List<JobListing> jobs, Guid userId)
    {
        lock (_jobsLock)
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
    }

    // For JSON, targeted operations do a read-modify-write of the full file.
    // Less efficient than SQL targeted ops, but still correct and fast enough for file I/O.

    public void SaveJob(JobListing job)
    {
        lock (_jobsLock)
        {
            var allJobs = LoadAllJobsUnsafe();
            var index = allJobs.FindIndex(j => j.Id == job.Id);
            if (index >= 0)
                allJobs[index] = job;

            WriteAllJobsUnsafe(allJobs);
        }
    }

    public void AddJob(JobListing job)
    {
        lock (_jobsLock)
        {
            var allJobs = LoadAllJobsUnsafe();
            allJobs.Add(job);
            WriteAllJobsUnsafe(allJobs);
        }
    }

    public void DeleteJob(Guid id)
    {
        lock (_jobsLock)
        {
            var allJobs = LoadAllJobsUnsafe();
            allJobs.RemoveAll(j => j.Id == id);
            WriteAllJobsUnsafe(allJobs);
        }
    }

    public void DeleteAllJobs(Guid userId)
    {
        lock (_jobsLock)
        {
            var allJobs = LoadAllJobsUnsafe();
            allJobs.RemoveAll(j => j.UserId == userId);
            WriteAllJobsUnsafe(allJobs);
        }
    }

    // Internal helpers that assume _jobsLock is already held
    private List<JobListing> LoadAllJobsUnsafe()
    {
        try
        {
            if (File.Exists(_jobsFilePath))
            {
                var json = File.ReadAllText(_jobsFilePath);
                return JsonSerializer.Deserialize<List<JobListing>>(json, ReadOptions) ?? new List<JobListing>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading jobs from file");
        }
        return new List<JobListing>();
    }

    private void WriteAllJobsUnsafe(List<JobListing> allJobs)
    {
        var json = JsonSerializer.Serialize(allJobs, WriteOptions);
        File.WriteAllText(_jobsFilePath, json);
    }

    public void AddHistoryEntry(JobHistoryEntry entry)
    {
        lock (_historyLock)
        {
            var allHistory = LoadAllHistoryUnsafe();
            allHistory.Insert(0, entry);

            // Enforce per-user cap
            var userHistory = allHistory.Where(h => h.UserId == entry.UserId).Take(_historyMax).ToList();
            var otherHistory = allHistory.Where(h => h.UserId != entry.UserId).ToList();
            allHistory = otherHistory.Concat(userHistory).ToList();

            WriteAllHistoryUnsafe(allHistory);
        }
    }

    public void DeleteAllHistory(Guid userId)
    {
        lock (_historyLock)
        {
            var allHistory = LoadAllHistoryUnsafe();
            allHistory.RemoveAll(h => h.UserId == userId);
            WriteAllHistoryUnsafe(allHistory);
        }
    }

    public List<JobHistoryEntry> LoadHistory(Guid userId)
    {
        lock (_historyLock)
        {
            var allHistory = LoadAllHistoryUnsafe();
            return allHistory
                .Where(h => h.UserId == userId || h.UserId == Guid.Empty)
                .OrderByDescending(h => h.Timestamp)
                .ToList();
        }
    }

    public void SaveHistory(List<JobHistoryEntry> history, Guid userId)
    {
        lock (_historyLock)
        {
            var allHistory = LoadAllHistoryUnsafe();
            allHistory.RemoveAll(h => h.UserId == userId);
            allHistory.AddRange(history);
            WriteAllHistoryUnsafe(allHistory);
        }
    }

    // Internal helpers that assume _historyLock is already held
    private List<JobHistoryEntry> LoadAllHistoryUnsafe()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = File.ReadAllText(_historyFilePath);
                var history = JsonSerializer.Deserialize<List<JobHistoryEntry>>(json, ReadOptions);
                if (history != null)
                    return history;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading history from file");
        }
        return new List<JobHistoryEntry>();
    }

    private void WriteAllHistoryUnsafe(List<JobHistoryEntry> history)
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
        lock (_settingsLock)
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
    }

    public void SaveSettings(AppSettings settings, Guid userId)
    {
        lock (_settingsLock)
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
    }

    // Contact operations
    private class ContactStore
    {
        public List<Contact> Contacts { get; set; } = new();
        public List<JobContact> JobLinks { get; set; } = new();
    }

    private string GetContactsFilePath(Guid userId) => Path.Combine(_dataDirectory, $"contacts_{userId}.json");

    private ContactStore LoadContactStore(Guid userId)
    {
        var path = GetContactsFilePath(userId);
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ContactStore>(json, ReadOptions) ?? new ContactStore();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading contacts from file");
        }
        return new ContactStore();
    }

    private void WriteContactStore(Guid userId, ContactStore store)
    {
        try
        {
            var json = JsonSerializer.Serialize(store, WriteOptions);
            File.WriteAllText(GetContactsFilePath(userId), json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving contacts to file");
        }
    }

    public List<Contact> LoadContacts(Guid userId)
    {
        lock (_contactsLock)
        {
            return LoadContactStore(userId).Contacts;
        }
    }

    public Contact? GetContactById(Guid contactId)
    {
        lock (_contactsLock)
        {
            // Search all contact files
            foreach (var file in Directory.GetFiles(_dataDirectory, "contacts_*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var store = JsonSerializer.Deserialize<ContactStore>(json, ReadOptions);
                    var contact = store?.Contacts.FirstOrDefault(c => c.Id == contactId);
                    if (contact != null) return contact;
                }
                catch { }
            }
            return null;
        }
    }

    public void SaveContact(Contact contact)
    {
        lock (_contactsLock)
        {
            var store = LoadContactStore(contact.UserId);
            var index = store.Contacts.FindIndex(c => c.Id == contact.Id);
            if (index >= 0)
                store.Contacts[index] = contact;
            WriteContactStore(contact.UserId, store);
        }
    }

    public void AddContact(Contact contact)
    {
        lock (_contactsLock)
        {
            var store = LoadContactStore(contact.UserId);
            store.Contacts.Add(contact);
            WriteContactStore(contact.UserId, store);
        }
    }

    public void DeleteContact(Guid contactId)
    {
        lock (_contactsLock)
        {
            foreach (var file in Directory.GetFiles(_dataDirectory, "contacts_*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var store = JsonSerializer.Deserialize<ContactStore>(json, ReadOptions);
                    if (store == null) continue;
                    var removed = store.Contacts.RemoveAll(c => c.Id == contactId);
                    store.JobLinks.RemoveAll(jl => jl.ContactId == contactId);
                    if (removed > 0)
                    {
                        var storeJson = JsonSerializer.Serialize(store, WriteOptions);
                        File.WriteAllText(file, storeJson);
                        return;
                    }
                }
                catch { }
            }
        }
    }

    public List<Contact> GetContactsForJob(Guid jobId)
    {
        lock (_contactsLock)
        {
            foreach (var file in Directory.GetFiles(_dataDirectory, "contacts_*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var store = JsonSerializer.Deserialize<ContactStore>(json, ReadOptions);
                    if (store == null) continue;
                    var contactIds = store.JobLinks.Where(jl => jl.JobId == jobId).Select(jl => jl.ContactId).ToHashSet();
                    if (contactIds.Count > 0)
                        return store.Contacts.Where(c => contactIds.Contains(c.Id)).ToList();
                }
                catch { }
            }
            return new List<Contact>();
        }
    }

    public void LinkContactToJob(Guid contactId, Guid jobId)
    {
        lock (_contactsLock)
        {
            foreach (var file in Directory.GetFiles(_dataDirectory, "contacts_*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var store = JsonSerializer.Deserialize<ContactStore>(json, ReadOptions);
                    if (store == null) continue;
                    if (store.Contacts.Any(c => c.Id == contactId))
                    {
                        if (!store.JobLinks.Any(jl => jl.JobId == jobId && jl.ContactId == contactId))
                        {
                            store.JobLinks.Add(new JobContact { JobId = jobId, ContactId = contactId });
                            var storeJson = JsonSerializer.Serialize(store, WriteOptions);
                            File.WriteAllText(file, storeJson);
                        }
                        return;
                    }
                }
                catch { }
            }
        }
    }

    public void UnlinkContactFromJob(Guid contactId, Guid jobId)
    {
        lock (_contactsLock)
        {
            foreach (var file in Directory.GetFiles(_dataDirectory, "contacts_*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var store = JsonSerializer.Deserialize<ContactStore>(json, ReadOptions);
                    if (store == null) continue;
                    var removed = store.JobLinks.RemoveAll(jl => jl.JobId == jobId && jl.ContactId == contactId);
                    if (removed > 0)
                    {
                        var storeJson = JsonSerializer.Serialize(store, WriteOptions);
                        File.WriteAllText(file, storeJson);
                        return;
                    }
                }
                catch { }
            }
        }
    }

    public Contact? FindContactByName(Guid userId, string name)
    {
        lock (_contactsLock)
        {
            var store = LoadContactStore(userId);
            return store.Contacts.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public Dictionary<Guid, int> GetJobLinkCountsForContacts(List<Guid> contactIds)
    {
        lock (_contactsLock)
        {
            var result = new Dictionary<Guid, int>();
            foreach (var file in Directory.GetFiles(_dataDirectory, "contacts_*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var store = JsonSerializer.Deserialize<ContactStore>(json, ReadOptions);
                    if (store == null) continue;
                    foreach (var id in contactIds)
                    {
                        var count = store.JobLinks.Count(jl => jl.ContactId == id);
                        if (count > 0)
                            result[id] = count;
                    }
                }
                catch { }
            }
            return result;
        }
    }

    public List<Guid> GetLinkedJobIds(Guid contactId)
    {
        lock (_contactsLock)
        {
            foreach (var file in Directory.GetFiles(_dataDirectory, "contacts_*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var store = JsonSerializer.Deserialize<ContactStore>(json, ReadOptions);
                    if (store == null) continue;
                    var ids = store.JobLinks.Where(jl => jl.ContactId == contactId).Select(jl => jl.JobId).ToList();
                    if (ids.Count > 0) return ids;
                }
                catch { }
            }
            return new List<Guid>();
        }
    }

    /// <summary>
    /// Migrate existing data to the specified user
    /// </summary>
    public void MigrateExistingDataToUser(Guid userId)
    {
        // Migrate jobs
        lock (_jobsLock)
        {
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
        }

        // Migrate history
        lock (_historyLock)
        {
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
        }

        // Migrate settings - copy old settings file to user-specific file
        lock (_settingsLock)
        {
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
    }

    /// <summary>
    /// Whether JSON data files exist (used for import detection)
    /// </summary>
    public bool HasExistingData()
    {
        return File.Exists(_jobsFilePath) || File.Exists(_historyFilePath) || File.Exists(_settingsFilePath);
    }
}
