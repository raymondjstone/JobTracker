using JobTracker.Models;
using JobTracker.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobTracker.Tests;

public class JsonStorageBackendTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonStorageBackend _storage;
    private readonly Guid _userId = Guid.NewGuid();

    public JsonStorageBackendTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JobTrackerTests_" + Guid.NewGuid().ToString("N"));
        var logger = NullLogger<JsonStorageBackend>.Instance;
        _storage = new JsonStorageBackend(_tempDir, logger, 1000);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    // === User Operations ===

    [Fact]
    public void AddUser_And_GetById()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "test@example.com" };
        _storage.AddUser(user);

        var retrieved = _storage.GetUserById(user.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("test@example.com", retrieved.Email);
    }

    [Fact]
    public void GetUserByEmail_CaseInsensitive()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "Test@Example.COM" };
        _storage.AddUser(user);

        var retrieved = _storage.GetUserByEmail("test@example.com");
        Assert.NotNull(retrieved);
        Assert.Equal(user.Id, retrieved.Id);
    }

    [Fact]
    public void SaveUser_UpdatesExisting()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "old@example.com" };
        _storage.AddUser(user);

        user.Email = "new@example.com";
        _storage.SaveUser(user);

        var retrieved = _storage.GetUserById(user.Id);
        Assert.Equal("new@example.com", retrieved!.Email);
        Assert.Single(_storage.GetAllUsers());
    }

    [Fact]
    public void DeleteUser_RemovesUser()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "delete@example.com" };
        _storage.AddUser(user);
        _storage.DeleteUser(user.Id);

        Assert.Null(_storage.GetUserById(user.Id));
    }

    // === Job Operations ===

    [Fact]
    public void AddJob_And_LoadJobs()
    {
        var job = new JobListing
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Title = "Developer",
            Company = "TestCo"
        };
        _storage.AddJob(job);

        var jobs = _storage.LoadJobs(_userId);
        Assert.Single(jobs);
        Assert.Equal("Developer", jobs[0].Title);
    }

    [Fact]
    public void SaveJob_UpdatesExisting()
    {
        var job = new JobListing
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Title = "Old Title",
            Company = "TestCo"
        };
        _storage.AddJob(job);

        job.Title = "New Title";
        _storage.SaveJob(job);

        var jobs = _storage.LoadJobs(_userId);
        Assert.Single(jobs);
        Assert.Equal("New Title", jobs[0].Title);
    }

    [Fact]
    public void DeleteJob_RemovesJob()
    {
        var job = new JobListing { Id = Guid.NewGuid(), UserId = _userId, Title = "Delete Me", Company = "X" };
        _storage.AddJob(job);
        _storage.DeleteJob(job.Id);

        Assert.Empty(_storage.LoadJobs(_userId));
    }

    [Fact]
    public void DeleteAllJobs_RemovesOnlyUserJobs()
    {
        var otherUserId = Guid.NewGuid();
        _storage.AddJob(new JobListing { Id = Guid.NewGuid(), UserId = _userId, Title = "Mine", Company = "A" });
        _storage.AddJob(new JobListing { Id = Guid.NewGuid(), UserId = otherUserId, Title = "Theirs", Company = "B" });

        _storage.DeleteAllJobs(_userId);

        Assert.Empty(_storage.LoadJobs(_userId));
        Assert.Single(_storage.LoadJobs(otherUserId));
    }

    [Fact]
    public void LoadJobs_IsolatesUsers()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        _storage.AddJob(new JobListing { Id = Guid.NewGuid(), UserId = user1, Title = "Job1", Company = "A" });
        _storage.AddJob(new JobListing { Id = Guid.NewGuid(), UserId = user2, Title = "Job2", Company = "B" });

        Assert.Single(_storage.LoadJobs(user1));
        Assert.Single(_storage.LoadJobs(user2));
        Assert.Equal("Job1", _storage.LoadJobs(user1)[0].Title);
        Assert.Equal("Job2", _storage.LoadJobs(user2)[0].Title);
    }

    [Fact]
    public void SaveJobs_ReplacesUserJobs()
    {
        _storage.AddJob(new JobListing { Id = Guid.NewGuid(), UserId = _userId, Title = "Old", Company = "A" });

        var newJobs = new List<JobListing>
        {
            new JobListing { Id = Guid.NewGuid(), UserId = _userId, Title = "New1", Company = "B" },
            new JobListing { Id = Guid.NewGuid(), UserId = _userId, Title = "New2", Company = "C" }
        };
        _storage.SaveJobs(newJobs, _userId);

        var loaded = _storage.LoadJobs(_userId);
        Assert.Equal(2, loaded.Count);
    }

    // === History Operations ===

    [Fact]
    public void AddHistoryEntry_And_LoadHistory()
    {
        var entry = new JobHistoryEntry
        {
            UserId = _userId,
            JobTitle = "Test Job",
            ActionType = HistoryActionType.JobAdded,
            Timestamp = DateTime.Now
        };
        _storage.AddHistoryEntry(entry);

        var history = _storage.LoadHistory(_userId);
        Assert.Single(history);
        Assert.Equal("Test Job", history[0].JobTitle);
    }

    [Fact]
    public void DeleteAllHistory_RemovesOnlyUserHistory()
    {
        var otherUserId = Guid.NewGuid();
        _storage.AddHistoryEntry(new JobHistoryEntry { UserId = _userId, JobTitle = "Mine", Timestamp = DateTime.Now });
        _storage.AddHistoryEntry(new JobHistoryEntry { UserId = otherUserId, JobTitle = "Theirs", Timestamp = DateTime.Now });

        _storage.DeleteAllHistory(_userId);

        Assert.Empty(_storage.LoadHistory(_userId));
        Assert.Single(_storage.LoadHistory(otherUserId));
    }

    // === Settings Operations ===

    [Fact]
    public void SaveSettings_And_LoadSettings()
    {
        var settings = new AppSettings { EmailCheckEnabled = true, ImapHost = "imap.test.com" };
        _storage.SaveSettings(settings, _userId);

        var loaded = _storage.LoadSettings(_userId);
        Assert.True(loaded.EmailCheckEnabled);
        Assert.Equal("imap.test.com", loaded.ImapHost);
    }

    [Fact]
    public void LoadSettings_ReturnsDefaultsWhenNoFile()
    {
        var settings = _storage.LoadSettings(Guid.NewGuid());
        Assert.NotNull(settings);
        Assert.False(settings.EmailCheckEnabled);
    }

    // === Contact Operations ===

    [Fact]
    public void AddContact_And_LoadContacts()
    {
        var contact = new Contact { Id = Guid.NewGuid(), UserId = _userId, Name = "John", Email = "john@test.com" };
        _storage.AddContact(contact);

        var contacts = _storage.LoadContacts(_userId);
        Assert.Single(contacts);
        Assert.Equal("John", contacts[0].Name);
    }

    [Fact]
    public void LinkContactToJob_And_GetContactsForJob()
    {
        var contact = new Contact { Id = Guid.NewGuid(), UserId = _userId, Name = "Jane" };
        _storage.AddContact(contact);

        var jobId = Guid.NewGuid();
        _storage.LinkContactToJob(contact.Id, jobId);

        var jobContacts = _storage.GetContactsForJob(jobId);
        Assert.Single(jobContacts);
        Assert.Equal("Jane", jobContacts[0].Name);
    }

    [Fact]
    public void UnlinkContactFromJob_RemovesLink()
    {
        var contact = new Contact { Id = Guid.NewGuid(), UserId = _userId, Name = "Bob" };
        _storage.AddContact(contact);
        var jobId = Guid.NewGuid();
        _storage.LinkContactToJob(contact.Id, jobId);
        _storage.UnlinkContactFromJob(contact.Id, jobId);

        Assert.Empty(_storage.GetContactsForJob(jobId));
    }

    [Fact]
    public void DeleteContact_RemovesContactAndLinks()
    {
        var contact = new Contact { Id = Guid.NewGuid(), UserId = _userId, Name = "DeleteMe" };
        _storage.AddContact(contact);
        var jobId = Guid.NewGuid();
        _storage.LinkContactToJob(contact.Id, jobId);

        _storage.DeleteContact(contact.Id);

        Assert.Empty(_storage.LoadContacts(_userId));
        Assert.Empty(_storage.GetContactsForJob(jobId));
    }

    [Fact]
    public void FindContactByName_CaseInsensitive()
    {
        var contact = new Contact { Id = Guid.NewGuid(), UserId = _userId, Name = "Alice Smith" };
        _storage.AddContact(contact);

        var found = _storage.FindContactByName(_userId, "alice smith");
        Assert.NotNull(found);
        Assert.Equal(contact.Id, found.Id);
    }

    // === Processed Emails ===

    [Fact]
    public void SaveAndLoadProcessedEmails()
    {
        var emails = new List<ProcessedEmail>
        {
            new ProcessedEmail { MessageId = "msg1", ProcessedAt = DateTime.Now },
            new ProcessedEmail { MessageId = "msg2", ProcessedAt = DateTime.Now }
        };
        _storage.SaveProcessedEmails(emails, _userId);

        var loaded = _storage.LoadProcessedEmails(_userId);
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void SaveProcessedEmails_PrunesOldEntries()
    {
        var emails = new List<ProcessedEmail>
        {
            new ProcessedEmail { MessageId = "old", ProcessedAt = DateTime.Now.AddDays(-100) },
            new ProcessedEmail { MessageId = "recent", ProcessedAt = DateTime.Now }
        };
        _storage.SaveProcessedEmails(emails, _userId);

        var loaded = _storage.LoadProcessedEmails(_userId);
        Assert.Single(loaded);
        Assert.Equal("recent", loaded[0].MessageId);
    }

    // === Edge Cases ===

    [Fact]
    public void LoadJobs_EmptyFile_ReturnsEmptyList()
    {
        var jobs = _storage.LoadJobs(_userId);
        Assert.Empty(jobs);
    }

    [Fact]
    public void HasExistingData_FalseWhenEmpty()
    {
        Assert.False(_storage.HasExistingData());
    }

    [Fact]
    public void HasExistingData_TrueAfterAddingJobs()
    {
        _storage.AddJob(new JobListing { Id = Guid.NewGuid(), UserId = _userId, Title = "X", Company = "Y" });
        Assert.True(_storage.HasExistingData());
    }

    [Fact]
    public void GetDataDirectory_ReturnsCorrectPath()
    {
        Assert.Equal(_tempDir, _storage.GetDataDirectory());
    }
}
