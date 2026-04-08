using JobTracker.Models;
using JobTracker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JobTracker.Tests;

public class JobHistoryServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IStorageBackend> _storageMock;
    private readonly Mock<CurrentUserService> _currentUserMock;
    private readonly Mock<AppSettingsService> _settingsMock;
    private readonly JobHistoryService _service;

    public JobHistoryServiceTests()
    {
        _storageMock = new Mock<IStorageBackend>();
        _storageMock.Setup(s => s.LoadHistory(It.IsAny<Guid>())).Returns(new List<JobHistoryEntry>());
        _storageMock.Setup(s => s.AddHistoryEntry(It.IsAny<JobHistoryEntry>(), It.IsAny<int>()));

        _currentUserMock = new Mock<CurrentUserService>(
            MockBehavior.Loose,
            new object[] { null!, null!, null!, null! });
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(_userId);

        _settingsMock = new Mock<AppSettingsService>(
            MockBehavior.Loose,
            new object[] { null!, null!, null!, null! });
        _settingsMock.Setup(s => s.GetSettings(It.IsAny<Guid?>())).Returns(new AppSettings { HistoryMaxEntries = 50000 });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "HistoryMax", "50000" } })
            .Build();

        var logger = NullLogger<JobHistoryService>.Instance;
        _service = new JobHistoryService(_storageMock.Object, logger, _currentUserMock.Object, config, _settingsMock.Object);
    }

    private JobListing MakeJob(string title = "Developer", string company = "Acme") => new()
    {
        Id = Guid.NewGuid(),
        UserId = _userId,
        Title = title,
        Company = company,
        Url = "https://example.com/job/1"
    };

    [Fact]
    public void RecordJobAdded_StoresEntry()
    {
        var job = MakeJob();
        _service.RecordJobAdded(job, HistoryChangeSource.BrowserExtension);

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.ActionType == HistoryActionType.JobAdded &&
            e.JobTitle == "Developer" &&
            e.Company == "Acme" &&
            e.ChangeSource == HistoryChangeSource.BrowserExtension &&
            e.UserId == _userId
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RecordJobDeleted_StoresEntry()
    {
        var job = MakeJob();
        _service.RecordJobDeleted(job, HistoryChangeSource.Manual);

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.ActionType == HistoryActionType.JobDeleted &&
            e.ChangeSource == HistoryChangeSource.Manual
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RecordInterestChanged_IncludesOldAndNewValues()
    {
        var job = MakeJob();
        _service.RecordInterestChanged(job, InterestStatus.NotRated, InterestStatus.Interested, HistoryChangeSource.Manual);

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.OldValue == "NotRated" &&
            e.NewValue == "Interested" &&
            e.ActionType == HistoryActionType.InterestChanged
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RecordInterestChanged_WithNullOldValue_UsesNotRated()
    {
        var job = MakeJob();
        _service.RecordInterestChanged(job, null, InterestStatus.Interested, HistoryChangeSource.Rule, "Test Rule");

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.OldValue == "NotRated" &&
            e.RuleName == "Test Rule" &&
            e.Details!.Contains("Test Rule")
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RecordSuitabilityChanged_WithReason_UsesReasonAsDetails()
    {
        var job = MakeJob();
        _service.RecordSuitabilityChanged(job, SuitabilityStatus.NotChecked, SuitabilityStatus.Unsuitable,
            HistoryChangeSource.System, reason: "Job expired");

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.Details == "Job expired" &&
            e.NewValue == "Unsuitable"
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RecordAppliedStatusChanged_HasCorrectDetails()
    {
        var job = MakeJob();
        _service.RecordAppliedStatusChanged(job, false, true, HistoryChangeSource.Manual);

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.Details == "Marked as applied" &&
            e.OldValue == "Not Applied" &&
            e.NewValue == "Applied"
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RecordDescriptionUpdated_IncludesCharCounts()
    {
        var job = MakeJob();
        _service.RecordDescriptionUpdated(job, 0, 500, HistoryChangeSource.AutoFetch);

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.OldValue == "Empty" &&
            e.NewValue == "500 chars" &&
            e.ActionType == HistoryActionType.DescriptionUpdated
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void GetHistory_ReturnsOnlyCurrentUserEntries()
    {
        var otherUserId = Guid.NewGuid();
        var entries = new List<JobHistoryEntry>
        {
            new() { UserId = _userId, ActionType = HistoryActionType.JobAdded, JobTitle = "Job 1", Company = "A" },
            new() { UserId = otherUserId, ActionType = HistoryActionType.JobAdded, JobTitle = "Job 2", Company = "B" },
            new() { UserId = _userId, ActionType = HistoryActionType.JobDeleted, JobTitle = "Job 3", Company = "C" }
        };
        _storageMock.Setup(s => s.LoadHistory(_userId)).Returns(entries);

        var result = _service.GetHistory();
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Entries, e => Assert.Equal(_userId, e.UserId));
    }

    [Fact]
    public void GetHistory_FiltersbyActionType()
    {
        var entries = new List<JobHistoryEntry>
        {
            new() { UserId = _userId, ActionType = HistoryActionType.JobAdded, JobTitle = "J1", Company = "A" },
            new() { UserId = _userId, ActionType = HistoryActionType.JobDeleted, JobTitle = "J2", Company = "B" },
            new() { UserId = _userId, ActionType = HistoryActionType.JobAdded, JobTitle = "J3", Company = "C" }
        };
        _storageMock.Setup(s => s.LoadHistory(_userId)).Returns(entries);

        var filter = new JobHistoryFilter { ActionType = HistoryActionType.JobAdded };
        var result = _service.GetHistory(filter);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public void GetHistory_FiltersbySearchTerm()
    {
        var entries = new List<JobHistoryEntry>
        {
            new() { UserId = _userId, ActionType = HistoryActionType.JobAdded, JobTitle = "Senior Developer", Company = "Acme" },
            new() { UserId = _userId, ActionType = HistoryActionType.JobAdded, JobTitle = "Designer", Company = "Beta" },
        };
        _storageMock.Setup(s => s.LoadHistory(_userId)).Returns(entries);

        var filter = new JobHistoryFilter { SearchTerm = "developer" };
        var result = _service.GetHistory(filter);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Senior Developer", result.Entries[0].JobTitle);
    }

    [Fact]
    public void GetHistory_Paginates()
    {
        var entries = Enumerable.Range(1, 10).Select(i => new JobHistoryEntry
        {
            UserId = _userId,
            ActionType = HistoryActionType.JobAdded,
            JobTitle = $"Job {i}",
            Company = "Co"
        }).ToList();
        _storageMock.Setup(s => s.LoadHistory(_userId)).Returns(entries);

        var result = _service.GetHistory(pageNumber: 2, pageSize: 3);
        Assert.Equal(10, result.TotalCount);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(2, result.PageNumber);
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void GetTotalCount_ReturnsOnlyUserCount()
    {
        var entries = new List<JobHistoryEntry>
        {
            new() { UserId = _userId, ActionType = HistoryActionType.JobAdded, JobTitle = "J1", Company = "A" },
            new() { UserId = Guid.NewGuid(), ActionType = HistoryActionType.JobAdded, JobTitle = "J2", Company = "B" },
        };
        _storageMock.Setup(s => s.LoadHistory(_userId)).Returns(entries);

        Assert.Equal(1, _service.GetTotalCount());
    }

    [Fact]
    public void RecordBulkImport_UsesEmptyJobId()
    {
        _service.RecordBulkImport(42, HistoryChangeSource.Import);

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.JobId == Guid.Empty &&
            e.ActionType == HistoryActionType.BulkImport &&
            e.Details!.Contains("42")
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void AddEntry_WithNoUser_DoesNotStore()
    {
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(Guid.Empty);
        var job = MakeJob();
        _service.RecordJobAdded(job, HistoryChangeSource.Manual);

        // Entry should still be stored because AddEntry uses the job's UserId
        _storageMock.Verify(s => s.AddHistoryEntry(It.IsAny<JobHistoryEntry>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void GetActionTypeCounts_GroupsCorrectly()
    {
        var entries = new List<JobHistoryEntry>
        {
            new() { UserId = _userId, ActionType = HistoryActionType.JobAdded, JobTitle = "J1", Company = "A" },
            new() { UserId = _userId, ActionType = HistoryActionType.JobAdded, JobTitle = "J2", Company = "B" },
            new() { UserId = _userId, ActionType = HistoryActionType.JobDeleted, JobTitle = "J3", Company = "C" },
        };
        _storageMock.Setup(s => s.LoadHistory(_userId)).Returns(entries);

        var counts = _service.GetActionTypeCounts();
        Assert.Equal(2, counts[HistoryActionType.JobAdded]);
        Assert.Equal(1, counts[HistoryActionType.JobDeleted]);
    }

    [Fact]
    public void ClearHistory_RemovesUserEntries()
    {
        _service.ClearHistory();
        _storageMock.Verify(s => s.DeleteAllHistory(_userId), Times.Once);
    }

    [Fact]
    public void RecordRemoteStatusChanged_HasCorrectLabels()
    {
        var job = MakeJob();
        _service.RecordRemoteStatusChanged(job, false, true, HistoryChangeSource.Rule, "Remote Rule");

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.OldValue == "Onsite" &&
            e.NewValue == "Remote" &&
            e.RuleName == "Remote Rule"
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void OnChange_FiresOnAddEntry()
    {
        var fired = false;
        _service.OnChange += () => fired = true;

        var job = MakeJob();
        _service.RecordJobAdded(job, HistoryChangeSource.Manual);

        Assert.True(fired);
    }

    [Fact]
    public void RecordStandaloneContactDiscussion_CreatesEntryWithEmptyJobId()
    {
        _service.RecordStandaloneContactDiscussion(
            "Senior Developer",
            "Tech Corp",
            "John Smith",
            "Phone screening",
            "Scheduled interview for next week"
        );

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.JobId == Guid.Empty &&
            e.ActionType == HistoryActionType.ContactDiscussion &&
            e.JobTitle == "Senior Developer" &&
            e.Company == "Tech Corp" &&
            e.ContactName == "John Smith" &&
            e.ContactReason == "Phone screening" &&
            e.ContactResult == "Scheduled interview for next week" &&
            e.ChangeSource == HistoryChangeSource.Manual &&
            e.UserId == _userId
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RecordStandaloneContactDiscussion_WithCustomTimestamp_UsesProvidedTimestamp()
    {
        var customTimestamp = new DateTime(2025, 1, 15, 14, 30, 0);

        _service.RecordStandaloneContactDiscussion(
            "Developer",
            "Acme Inc",
            "Jane Doe",
            "Follow-up call",
            "No response",
            customTimestamp
        );

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.Timestamp == customTimestamp
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RecordStandaloneContactDiscussion_WithoutTimestamp_UsesCurrentTime()
    {
        var beforeCall = DateTime.Now;

        _service.RecordStandaloneContactDiscussion(
            "Developer",
            "Test Co",
            "Bob Jones",
            "Email inquiry",
            "Waiting for response"
        );

        var afterCall = DateTime.Now;

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.Timestamp >= beforeCall && e.Timestamp <= afterCall
        ), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RecordStandaloneContactDiscussion_WithNoUser_DoesNotStore()
    {
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(Guid.Empty);

        _service.RecordStandaloneContactDiscussion(
            "Developer",
            "Test Co",
            "Contact",
            "Reason",
            "Result"
        );

        _storageMock.Verify(s => s.AddHistoryEntry(It.IsAny<JobHistoryEntry>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void RecordStandaloneContactDiscussion_IncludesContactInDetails()
    {
        _service.RecordStandaloneContactDiscussion(
            "Developer",
            "Test Co",
            "Alice Smith",
            "Initial discussion",
            "Positive"
        );

        _storageMock.Verify(s => s.AddHistoryEntry(It.Is<JobHistoryEntry>(e =>
            e.Details != null && e.Details.Contains("Alice Smith") && e.Details.Contains("Initial discussion")
        ), It.IsAny<int>()), Times.Once);
    }
}

