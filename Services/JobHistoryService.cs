using JobTracker.Models;
using Microsoft.Extensions.Configuration;

namespace JobTracker.Services;

public class JobHistoryService
{
    private readonly ILogger<JobHistoryService> _logger;
    private readonly IStorageBackend _storage;
    private readonly CurrentUserService _currentUser;
    private readonly int _historyLimit;
    private readonly object _lock = new();
    private List<JobHistoryEntry> _history = new();
    private Guid _loadedForUser = Guid.Empty;

    public event Action? OnChange;

    public JobHistoryService(IStorageBackend storage, ILogger<JobHistoryService> logger, CurrentUserService currentUser, IConfiguration configuration)
    {
        _logger = logger;
        _storage = storage;
        _currentUser = currentUser;
        _historyLimit = configuration.GetValue<int>("HistoryMax", 50000);
        // Don't load here - will load lazily when needed
    }

    private Guid CurrentUserId => _currentUser.GetCurrentUserId();

    private void EnsureHistoryLoaded()
    {
        var userId = CurrentUserId;
        if (userId == Guid.Empty) return;

        lock (_lock)
        {
            // Only reload if user changed or history not loaded yet
            if (_loadedForUser != userId || !_history.Any())
            {
                _history = _storage.LoadHistory(userId);
                _loadedForUser = userId;
                _logger.LogDebug("Loaded {Count} history entries for user {UserId}", _history.Count, userId);
            }
        }
    }

    /// <summary>
    /// Forces a reload from storage, bypassing the cache
    /// </summary>
    public void ForceReload()
    {
        var userId = CurrentUserId;
        if (userId == Guid.Empty) return;

        lock (_lock)
        {
            _history = _storage.LoadHistory(userId);
            _loadedForUser = userId;
            _logger.LogDebug("Force reloaded {Count} history entries for user {UserId}", _history.Count, userId);
        }
    }

    public void RecordJobAdded(JobListing job, HistoryChangeSource source)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.JobAdded,
            ChangeSource = source,
            Details = $"Job added from {job.Source}"
        }, job.UserId);  // Pass the job's user ID
    }

    public void RecordJobDeleted(JobListing job, HistoryChangeSource source)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.JobDeleted,
            ChangeSource = source,
            Details = "Job removed from tracker"
        }, job.UserId);
    }

    public void RecordDescriptionUpdated(JobListing job, int oldLength, int newLength, HistoryChangeSource source)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.DescriptionUpdated,
            ChangeSource = source,
            OldValue = oldLength > 0 ? $"{oldLength} chars" : "Empty",
            NewValue = $"{newLength} chars",
            Details = $"Description updated ({oldLength} -> {newLength} chars)"
        }, job.UserId);
    }

    public void RecordInterestChanged(JobListing job, InterestStatus? oldValue, InterestStatus newValue,
        HistoryChangeSource source, string? ruleName = null)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.InterestChanged,
            ChangeSource = source,
            OldValue = oldValue?.ToString() ?? "NotRated",
            NewValue = newValue.ToString(),
            RuleName = ruleName,
            Details = ruleName != null
                ? $"Interest set to {newValue} by rule: {ruleName}"
                : $"Interest changed to {newValue}"
        }, job.UserId);
    }

    public void RecordSuitabilityChanged(JobListing job, SuitabilityStatus? oldValue, SuitabilityStatus newValue,
        HistoryChangeSource source, string? ruleName = null, string? reason = null)
    {
        var details = reason
            ?? (ruleName != null
                ? $"Suitability set to {newValue} by rule: {ruleName}"
                : $"Suitability changed to {newValue}");

        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.SuitabilityChanged,
            ChangeSource = source,
            OldValue = oldValue?.ToString() ?? "NotChecked",
            NewValue = newValue.ToString(),
            RuleName = ruleName,
            Details = details
        }, job.UserId);
    }

    public void RecordRemoteStatusChanged(JobListing job, bool oldValue, bool newValue,
        HistoryChangeSource source, string? ruleName = null)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.RemoteStatusChanged,
            ChangeSource = source,
            OldValue = oldValue ? "Remote" : "Onsite",
            NewValue = newValue ? "Remote" : "Onsite",
            RuleName = ruleName,
            Details = ruleName != null
                ? $"Remote status set to {(newValue ? "Remote" : "Onsite")} by rule: {ruleName}"
                : $"Remote status changed to {(newValue ? "Remote" : "Onsite")}"
        }, job.UserId);
    }

    public void RecordAppliedStatusChanged(JobListing job, bool oldValue, bool newValue, HistoryChangeSource source)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.AppliedStatusChanged,
            ChangeSource = source,
            OldValue = oldValue ? "Applied" : "Not Applied",
            NewValue = newValue ? "Applied" : "Not Applied",
            Details = newValue ? "Marked as applied" : "Unmarked as applied"
        }, job.UserId);
    }

    public void RecordApplicationStageChanged(JobListing job, string? oldStage, string? newStage, HistoryChangeSource source)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.ApplicationStageChanged,
            ChangeSource = source,
            OldValue = string.IsNullOrEmpty(oldStage) ? "None" : oldStage,
            NewValue = string.IsNullOrEmpty(newStage) ? "None" : newStage,
            Details = $"Application stage: {oldStage ?? "None"} -> {newStage ?? "None"}"
        }, job.UserId);
    }

    public void RecordBulkImport(int count, HistoryChangeSource source)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = Guid.Empty,
            JobTitle = "Bulk Import",
            Company = "",
            ActionType = HistoryActionType.BulkImport,
            ChangeSource = source,
            Details = $"Imported {count} jobs"
        });
    }

    public void RecordDuplicateRemoved(JobListing job)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.DuplicateRemoved,
            ChangeSource = HistoryChangeSource.System,
            Details = "Removed as duplicate"
        }, job.UserId);
    }

    public void RecordNotesUpdated(JobListing job, HistoryChangeSource source)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = job.Company,
            JobUrl = job.Url,
            ActionType = HistoryActionType.NotesUpdated,
            ChangeSource = source,
            Details = "Notes updated"
        }, job.UserId);
    }

    public void RecordCompanyUpdated(JobListing job, string? oldCompany, string newCompany, HistoryChangeSource source)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = job.Id,
            JobTitle = job.Title,
            Company = newCompany,
            JobUrl = job.Url,
            ActionType = HistoryActionType.CompanyUpdated,
            ChangeSource = source,
            OldValue = string.IsNullOrWhiteSpace(oldCompany) ? "Empty" : oldCompany,
            NewValue = newCompany,
            Details = $"Company set to '{newCompany}'"
        }, job.UserId);
    }

    public void RecordChange(Guid jobId, Guid userId, string fieldName, string? oldValue, string? newValue, string description, string jobTitle, string company, string? jobUrl)
    {
        AddEntry(new JobHistoryEntry
        {
            JobId = jobId,
            JobTitle = jobTitle,
            Company = company,
            JobUrl = jobUrl,
            ActionType = HistoryActionType.Modified,
            ChangeSource = HistoryChangeSource.System,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            Details = description
        }, userId);
    }

    public List<JobHistoryEntry> GetHistoryByJobId(Guid jobId)
    {
        EnsureHistoryLoaded();
        var userId = CurrentUserId;

        lock (_lock)
        {
            return _history
                .Where(e => e.JobId == jobId && e.UserId == userId)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
    }

    private void AddEntry(JobHistoryEntry entry, Guid? forUserId = null)
    {
        var userId = forUserId ?? CurrentUserId;
        if (userId == Guid.Empty)
        {
            _logger.LogWarning("Cannot add history entry - no user context. Entry: {ActionType} - {Title}", 
                entry.ActionType, entry.JobTitle);
            return;
        }

        entry.UserId = userId;

        lock (_lock)
        {
            _history.Insert(0, entry); // Add to beginning for reverse chronological

            // Keep history manageable - cap at HistoryLimit entries per user
            var userHistory = _history.Where(h => h.UserId == userId).ToList();
            if (userHistory.Count > _historyLimit)
            {
                var toRemove = userHistory.Skip(_historyLimit).Select(h => h.Id).ToHashSet();
                _history.RemoveAll(h => toRemove.Contains(h.Id));
            }

            _storage.AddHistoryEntry(entry);
        }

        _logger.LogDebug("History: {Action} - {Title} ({Source}) for user {UserId}",
            entry.ActionType, entry.JobTitle, entry.ChangeSource, userId);

        OnChange?.Invoke();
    }

    public PagedHistoryResult GetHistory(JobHistoryFilter? filter = null, int pageNumber = 1, int pageSize = 50)
    {
        EnsureHistoryLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            var query = _history.Where(h => h.UserId == userId).AsEnumerable();

            if (filter != null)
            {
                if (filter.ActionType.HasValue)
                    query = query.Where(h => h.ActionType == filter.ActionType.Value);

                if (filter.ChangeSource.HasValue)
                    query = query.Where(h => h.ChangeSource == filter.ChangeSource.Value);

                if (filter.FromDate.HasValue)
                    query = query.Where(h => h.Timestamp >= filter.FromDate.Value);

                if (filter.ToDate.HasValue)
                    query = query.Where(h => h.Timestamp <= filter.ToDate.Value.AddDays(1));

                if (filter.JobId.HasValue && filter.JobId.Value != Guid.Empty)
                    query = query.Where(h => h.JobId == filter.JobId.Value);

                if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
                {
                    var term = filter.SearchTerm.ToLowerInvariant();
                    query = query.Where(h =>
                        h.JobTitle.ToLowerInvariant().Contains(term) ||
                        h.Company.ToLowerInvariant().Contains(term) ||
                        (h.Details?.ToLowerInvariant().Contains(term) ?? false) ||
                        (h.RuleName?.ToLowerInvariant().Contains(term) ?? false));
                }
            }

            var totalCount = query.Count();
            var entries = query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return new PagedHistoryResult
            {
                Entries = entries,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
    }

    public List<JobHistoryEntry> GetJobHistory(Guid jobId)
    {
        EnsureHistoryLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            return _history.Where(h => h.UserId == userId && h.JobId == jobId).ToList();
        }
    }

    public Dictionary<HistoryActionType, int> GetActionTypeCounts()
    {
        EnsureHistoryLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            return _history
                .Where(h => h.UserId == userId)
                .GroupBy(h => h.ActionType)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    public Dictionary<HistoryChangeSource, int> GetChangeSourceCounts()
    {
        EnsureHistoryLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            return _history
                .Where(h => h.UserId == userId)
                .GroupBy(h => h.ChangeSource)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    public int GetTotalCount()
    {
        EnsureHistoryLoaded();
        var userId = CurrentUserId;
        lock (_lock)
        {
            return _history.Count(h => h.UserId == userId);
        }
    }

    public void ClearHistory()
    {
        var userId = CurrentUserId;
        lock (_lock)
        {
            _history.RemoveAll(h => h.UserId == userId);
            _storage.DeleteAllHistory(userId);
        }
        OnChange?.Invoke();
        _logger.LogInformation("History cleared for user {UserId}", userId);
    }

}

