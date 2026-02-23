using JobTracker.Models;

namespace JobTracker.Services;

public interface IStorageBackend
{
    // User operations
    User? GetUserById(Guid id);
    User? GetUserByEmail(string email);
    User? GetUserByResetToken(string token);
    List<User> GetAllUsers();
    void SaveUser(User user);
    void AddUser(User user);
    void DeleteUser(Guid id);

    // Job operations (now user-scoped)
    List<JobListing> LoadJobs(Guid userId);
    void SaveJobs(List<JobListing> jobs, Guid userId);
    void SaveJob(JobListing job);
    void AddJob(JobListing job);
    void DeleteJob(Guid id);
    void DeleteAllJobs(Guid userId);

    // History operations (now user-scoped)
    List<JobHistoryEntry> LoadHistory(Guid userId);
    void SaveHistory(List<JobHistoryEntry> history, Guid userId);
    void AddHistoryEntry(JobHistoryEntry entry);
    void DeleteAllHistory(Guid userId);

    // Settings operations (now user-scoped)
    AppSettings LoadSettings(Guid userId);
    void SaveSettings(AppSettings settings, Guid userId);

    // Contact operations (user-scoped)
    List<Contact> LoadContacts(Guid userId);
    Contact? GetContactById(Guid contactId);
    void SaveContact(Contact contact);
    void AddContact(Contact contact);
    void DeleteContact(Guid contactId);
    List<Contact> GetContactsForJob(Guid jobId);
    void LinkContactToJob(Guid contactId, Guid jobId);
    void UnlinkContactFromJob(Guid contactId, Guid jobId);
    Contact? FindContactByName(Guid userId, string name);
    Dictionary<Guid, int> GetJobLinkCountsForContacts(List<Guid> contactIds);
    List<Guid> GetLinkedJobIds(Guid contactId);

    // Migration helpers
    void MigrateExistingDataToUser(Guid userId);
}
