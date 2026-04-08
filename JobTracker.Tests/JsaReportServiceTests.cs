using JobTracker.Models;
using JobTracker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JobTracker.Tests;

public class JsaReportServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IStorageBackend> _storageMock;
    private readonly Mock<CurrentUserService> _currentUserMock;
    private readonly Mock<AppSettingsService> _settingsMock;
    private readonly JobHistoryService _historyService;
    private readonly TestJobListingService _jobService;
    private readonly TestableJsaReportService _reportService;

    // Simple test double for JobListingService
    private class TestJobListingService
    {
        private readonly Dictionary<Guid, JobListing> _jobs = new();

        public void AddJob(Guid id, JobListing job) => _jobs[id] = job;

        public JobListing? GetJobById(Guid id)
        {
            if (_jobs.TryGetValue(id, out var job))
                return job;
            throw new InvalidOperationException("Job not found");
        }
    }

    // Testable version that accepts our test double
    private class TestableJsaReportService
    {
        private readonly JobHistoryService _historyService;
        private readonly TestJobListingService _jobService;

        public TestableJsaReportService(JobHistoryService historyService, TestJobListingService jobService)
        {
            _historyService = historyService;
            _jobService = jobService;
        }

        public List<JsaReportGroup> GenerateReport(JsaReportFilter filter)
        {
            _historyService.ForceReload();

            // Get all history (unpaged)
            var allHistory = _historyService.GetHistory(null, 1, int.MaxValue);

            var entries = allHistory.Entries.AsEnumerable();

            // Filter by selected action types
            if (filter.SelectedActionTypes.Count > 0)
                entries = entries.Where(e => filter.SelectedActionTypes.Contains(e.ActionType));

            // Filter by date range
            if (filter.FromDate.HasValue)
                entries = entries.Where(e => e.Timestamp >= filter.FromDate.Value);
            if (filter.ToDate.HasValue)
                entries = entries.Where(e => e.Timestamp <= filter.ToDate.Value.AddDays(1));

            // Filter by change source
            if (filter.ChangeSource.HasValue)
                entries = entries.Where(e => e.ChangeSource == filter.ChangeSource.Value);

            // Filter by search term
            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.ToLowerInvariant();
                entries = entries.Where(e =>
                    e.JobTitle.ToLowerInvariant().Contains(term) ||
                    e.Company.ToLowerInvariant().Contains(term) ||
                    (e.Details?.ToLowerInvariant().Contains(term) ?? false));
            }

            var filteredEntries = entries.ToList();

            // Group by JobId for job-related entries
            var jobGroups = filteredEntries
                .Where(e => e.JobId != Guid.Empty)
                .GroupBy(e => e.JobId)
                .Select(g =>
                {
                    var latestEntry = g.OrderByDescending(e => e.Timestamp).First();
                    JobListing? job = null;
                    try { job = _jobService.GetJobById(g.Key); } catch { }

                    return new JsaReportGroup
                    {
                        JobId = g.Key,
                        JobTitle = latestEntry.JobTitle,
                        Company = latestEntry.Company,
                        JobUrl = latestEntry.JobUrl,
                        Source = job?.Source ?? "",
                        LatestActivity = g.Max(e => e.Timestamp),
                        Entries = g.OrderByDescending(e => e.Timestamp).ToList(),
                        JobExists = job != null
                    };
                });

            // Handle standalone entries (JobId = Guid.Empty) - each gets its own group
            var standaloneGroups = filteredEntries
                .Where(e => e.JobId == Guid.Empty)
                .Select(e => new JsaReportGroup
                {
                    JobId = Guid.Empty,
                    JobTitle = e.JobTitle,
                    Company = e.Company,
                    JobUrl = e.JobUrl,
                    Source = "Standalone Entry",
                    LatestActivity = e.Timestamp,
                    Entries = new List<JobHistoryEntry> { e },
                    JobExists = false
                });

            // Combine and sort all groups
            return jobGroups.Concat(standaloneGroups)
                .OrderByDescending(g => g.LatestActivity)
                .ToList();
        }

        public JsaReportSummary GetSummary(List<JsaReportGroup> groups, JsaReportFilter filter)
        {
            var allEntries = groups.SelectMany(g => g.Entries).ToList();
            var summary = new JsaReportSummary
            {
                TotalJobs = groups.Count,
                TotalActivities = allEntries.Count,
                DateFrom = filter.FromDate ?? allEntries.MinBy(e => e.Timestamp)?.Timestamp.Date,
                DateTo = filter.ToDate ?? allEntries.MaxBy(e => e.Timestamp)?.Timestamp.Date,
                JobsAppliedTo = groups.Count(g => g.Entries.Any(e => e.ActionType == HistoryActionType.AppliedStatusChanged && e.NewValue == "Applied")),
                JobsAddedCount = allEntries.Count(e => e.ActionType == HistoryActionType.JobAdded),
                ActionTypeCounts = allEntries.GroupBy(e => e.ActionType).ToDictionary(g => g.Key, g => g.Count())
            };

            // Calculate weekly average
            if (summary.DateFrom.HasValue && summary.DateTo.HasValue)
            {
                var weeks = Math.Max(1, (summary.DateTo.Value - summary.DateFrom.Value).Days / 7.0);
                summary.ActivitiesPerWeek = Math.Round(allEntries.Count / weeks, 1);
            }

            return summary;
        }
    }

    public JsaReportServiceTests()
    {
        // Setup storage mock
        _storageMock = new Mock<IStorageBackend>();
        _storageMock.Setup(s => s.LoadHistory(It.IsAny<Guid>())).Returns(new List<JobHistoryEntry>());
        _storageMock.Setup(s => s.AddHistoryEntry(It.IsAny<JobHistoryEntry>(), It.IsAny<int>()));

        // Setup current user mock
        _currentUserMock = new Mock<CurrentUserService>(
            MockBehavior.Loose,
            new object[] { null!, null!, null!, null! });
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(_userId);

        // Setup settings mock
        _settingsMock = new Mock<AppSettingsService>(
            MockBehavior.Loose,
            new object[] { null!, null!, null!, null! });
        _settingsMock.Setup(s => s.GetSettings(It.IsAny<Guid?>())).Returns(new AppSettings { HistoryMaxEntries = 50000 });

        // Setup configuration
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "HistoryMax", "50000" } })
            .Build();

        // Create real JobHistoryService with mocked dependencies
        var logger = NullLogger<JobHistoryService>.Instance;
        _historyService = new JobHistoryService(_storageMock.Object, logger, _currentUserMock.Object, config, _settingsMock.Object);

        // Use test double for job service
        _jobService = new TestJobListingService();

        // Create testable report service
        _reportService = new TestableJsaReportService(_historyService, _jobService);
    }

    private void SetupHistoryEntries(List<JobHistoryEntry> entries)
    {
        _storageMock.Setup(s => s.LoadHistory(_userId)).Returns(entries);
        _historyService.ForceReload();
    }

    private JobHistoryEntry CreateHistoryEntry(
        Guid jobId,
        HistoryActionType actionType,
        string jobTitle = "Developer",
        string company = "Acme",
        DateTime? timestamp = null)
    {
        return new JobHistoryEntry
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            JobId = jobId,
            JobTitle = jobTitle,
            Company = company,
            ActionType = actionType,
            ChangeSource = HistoryChangeSource.Manual,
            Timestamp = timestamp ?? DateTime.Now,
            Details = $"{actionType} action"
        };
    }

    [Fact]
    public void GenerateReport_GroupsEntriesByJobId()
    {
        var jobId1 = Guid.NewGuid();
        var jobId2 = Guid.NewGuid();

        var entries = new List<JobHistoryEntry>
        {
            CreateHistoryEntry(jobId1, HistoryActionType.JobAdded, "Developer", "Acme"),
            CreateHistoryEntry(jobId1, HistoryActionType.AppliedStatusChanged, "Developer", "Acme"),
            CreateHistoryEntry(jobId2, HistoryActionType.JobAdded, "Designer", "Beta")
        };

        SetupHistoryEntries(entries);

        var filter = new JsaReportFilter();
        var groups = _reportService.GenerateReport(filter);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.JobId == jobId1 && g.Entries.Count == 2);
        Assert.Contains(groups, g => g.JobId == jobId2 && g.Entries.Count == 1);
    }

    [Fact]
    public void GenerateReport_IncludesStandaloneContactDiscussions()
    {
        var jobId = Guid.NewGuid();
        var entries = new List<JobHistoryEntry>
        {
            CreateHistoryEntry(jobId, HistoryActionType.JobAdded, "Developer", "Acme"),
            new JobHistoryEntry
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                JobId = Guid.Empty, // Standalone entry
                JobTitle = "Senior Engineer",
                Company = "Tech Corp",
                ActionType = HistoryActionType.ContactDiscussion,
                ChangeSource = HistoryChangeSource.Manual,
                ContactName = "John Smith",
                ContactReason = "Phone screening",
                ContactResult = "Scheduled interview",
                Timestamp = DateTime.Now
            }
        };

        SetupHistoryEntries(entries);

        var filter = new JsaReportFilter();
        var groups = _reportService.GenerateReport(filter);

        Assert.Equal(2, groups.Count);

        var standaloneGroup = groups.FirstOrDefault(g => g.JobId == Guid.Empty);
        Assert.NotNull(standaloneGroup);
        Assert.Equal("Senior Engineer", standaloneGroup.JobTitle);
        Assert.Equal("Tech Corp", standaloneGroup.Company);
        Assert.Equal("Standalone Entry", standaloneGroup.Source);
        Assert.Single(standaloneGroup.Entries);
        Assert.False(standaloneGroup.JobExists);
    }

    [Fact]
    public void GenerateReport_FiltersbySelectedActionTypes()
    {
        var jobId = Guid.NewGuid();
        var entries = new List<JobHistoryEntry>
        {
            CreateHistoryEntry(jobId, HistoryActionType.JobAdded),
            CreateHistoryEntry(jobId, HistoryActionType.AppliedStatusChanged),
            CreateHistoryEntry(jobId, HistoryActionType.NotesUpdated)
        };

        SetupHistoryEntries(entries);

        var filter = new JsaReportFilter
        {
            SelectedActionTypes = new HashSet<HistoryActionType>
            {
                HistoryActionType.JobAdded,
                HistoryActionType.AppliedStatusChanged
            }
        };

        var groups = _reportService.GenerateReport(filter);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Entries.Count);
        Assert.DoesNotContain(groups[0].Entries, e => e.ActionType == HistoryActionType.NotesUpdated);
    }

    [Fact]
    public void GenerateReport_FiltersbyDateRange()
    {
        var jobId = Guid.NewGuid();
        var baseDate = new DateTime(2025, 1, 15);
        var entries = new List<JobHistoryEntry>
        {
            CreateHistoryEntry(jobId, HistoryActionType.JobAdded, timestamp: baseDate.AddDays(-5)),
            CreateHistoryEntry(jobId, HistoryActionType.AppliedStatusChanged, timestamp: baseDate),
            CreateHistoryEntry(jobId, HistoryActionType.NotesUpdated, timestamp: baseDate.AddDays(5))
        };

        SetupHistoryEntries(entries);

        var filter = new JsaReportFilter
        {
            FromDate = baseDate.AddDays(-1),
            ToDate = baseDate.AddDays(1)
        };

        var groups = _reportService.GenerateReport(filter);

        Assert.Single(groups);
        Assert.Single(groups[0].Entries);
        Assert.Equal(HistoryActionType.AppliedStatusChanged, groups[0].Entries[0].ActionType);
    }

    [Fact]
    public void GenerateReport_FiltersbyChangeSource()
    {
        var jobId = Guid.NewGuid();
        var entries = new List<JobHistoryEntry>
        {
            new() { JobId = jobId, ActionType = HistoryActionType.JobAdded, ChangeSource = HistoryChangeSource.Manual, JobTitle = "Dev", Company = "A", UserId = _userId },
            new() { JobId = jobId, ActionType = HistoryActionType.AppliedStatusChanged, ChangeSource = HistoryChangeSource.BrowserExtension, JobTitle = "Dev", Company = "A", UserId = _userId }
        };

        SetupHistoryEntries(entries);

        var filter = new JsaReportFilter
        {
            ChangeSource = HistoryChangeSource.Manual
        };

        var groups = _reportService.GenerateReport(filter);

        Assert.Single(groups);
        Assert.Single(groups[0].Entries);
        Assert.Equal(HistoryChangeSource.Manual, groups[0].Entries[0].ChangeSource);
    }

    [Fact]
    public void GenerateReport_FiltersbySearchTerm()
    {
        var jobId1 = Guid.NewGuid();
        var jobId2 = Guid.NewGuid();
        var entries = new List<JobHistoryEntry>
        {
            CreateHistoryEntry(jobId1, HistoryActionType.JobAdded, "Senior Developer", "TechCorp"),
            CreateHistoryEntry(jobId2, HistoryActionType.JobAdded, "Designer", "CreativeCo")
        };

        SetupHistoryEntries(entries);

        var filter = new JsaReportFilter
        {
            SearchTerm = "developer"
        };

        var groups = _reportService.GenerateReport(filter);

        Assert.Single(groups);
        Assert.Equal("Senior Developer", groups[0].JobTitle);
    }

    [Fact]
    public void GenerateReport_OrdersByLatestActivityDescending()
    {
        var jobId1 = Guid.NewGuid();
        var jobId2 = Guid.NewGuid();
        var baseDate = DateTime.Now;

        var entries = new List<JobHistoryEntry>
        {
            CreateHistoryEntry(jobId1, HistoryActionType.JobAdded, timestamp: baseDate.AddDays(-5)),
            CreateHistoryEntry(jobId2, HistoryActionType.JobAdded, timestamp: baseDate.AddDays(-1))
        };

        SetupHistoryEntries(entries);

        var filter = new JsaReportFilter();
        var groups = _reportService.GenerateReport(filter);

        Assert.Equal(jobId2, groups[0].JobId); // Most recent first
        Assert.Equal(jobId1, groups[1].JobId);
    }

    [Fact]
    public void GenerateReport_SetsJobExistsWhenJobFound()
    {
        var jobId = Guid.NewGuid();
        var entries = new List<JobHistoryEntry>
        {
            CreateHistoryEntry(jobId, HistoryActionType.JobAdded)
        };

        SetupHistoryEntries(entries);

        _jobService.AddJob(jobId, new JobListing { Id = jobId, Source = "LinkedIn" });

        var filter = new JsaReportFilter();
        var groups = _reportService.GenerateReport(filter);

        Assert.Single(groups);
        Assert.True(groups[0].JobExists);
        Assert.Equal("LinkedIn", groups[0].Source);
    }

    [Fact]
    public void GenerateReport_SetsJobExistsFalseWhenJobNotFound()
    {
        var jobId = Guid.NewGuid();
        var entries = new List<JobHistoryEntry>
        {
            CreateHistoryEntry(jobId, HistoryActionType.JobAdded)
        };

        SetupHistoryEntries(entries);

        // Don't add job to stub - GetJobById will throw

        var filter = new JsaReportFilter();
        var groups = _reportService.GenerateReport(filter);

        Assert.Single(groups);
        Assert.False(groups[0].JobExists);
    }

    [Fact]
    public void GetSummary_CalculatesTotalJobs()
    {
        var groups = new List<JsaReportGroup>
        {
            new() { JobId = Guid.NewGuid(), Entries = new() },
            new() { JobId = Guid.NewGuid(), Entries = new() },
            new() { JobId = Guid.Empty, Entries = new() } // Standalone
        };

        var filter = new JsaReportFilter();
        var summary = _reportService.GetSummary(groups, filter);

        Assert.Equal(3, summary.TotalJobs);
    }

    [Fact]
    public void GetSummary_CalculatesTotalActivities()
    {
        var groups = new List<JsaReportGroup>
        {
            new() { Entries = new() { new JobHistoryEntry(), new JobHistoryEntry() } },
            new() { Entries = new() { new JobHistoryEntry() } }
        };

        var filter = new JsaReportFilter();
        var summary = _reportService.GetSummary(groups, filter);

        Assert.Equal(3, summary.TotalActivities);
    }

    [Fact]
    public void GetSummary_CountsJobsAppliedTo()
    {
        var groups = new List<JsaReportGroup>
        {
            new()
            {
                Entries = new()
                {
                    new() { ActionType = HistoryActionType.AppliedStatusChanged, NewValue = "Applied" }
                }
            },
            new()
            {
                Entries = new()
                {
                    new() { ActionType = HistoryActionType.JobAdded }
                }
            },
            new()
            {
                Entries = new()
                {
                    new() { ActionType = HistoryActionType.AppliedStatusChanged, NewValue = "Applied" }
                }
            }
        };

        var filter = new JsaReportFilter();
        var summary = _reportService.GetSummary(groups, filter);

        Assert.Equal(2, summary.JobsAppliedTo);
    }

    [Fact]
    public void GetSummary_CountsJobsAdded()
    {
        var groups = new List<JsaReportGroup>
        {
            new()
            {
                Entries = new()
                {
                    new() { ActionType = HistoryActionType.JobAdded },
                    new() { ActionType = HistoryActionType.AppliedStatusChanged }
                }
            },
            new()
            {
                Entries = new()
                {
                    new() { ActionType = HistoryActionType.JobAdded }
                }
            }
        };

        var filter = new JsaReportFilter();
        var summary = _reportService.GetSummary(groups, filter);

        Assert.Equal(2, summary.JobsAddedCount);
    }

    [Fact]
    public void GetSummary_CalculatesActivitiesPerWeek()
    {
        var fromDate = new DateTime(2025, 1, 1);
        var toDate = new DateTime(2025, 1, 15); // 14 days = 2 weeks

        var groups = new List<JsaReportGroup>
        {
            new() { Entries = new() { new JobHistoryEntry(), new JobHistoryEntry(), new JobHistoryEntry(), new JobHistoryEntry() } }
        };

        var filter = new JsaReportFilter { FromDate = fromDate, ToDate = toDate };
        var summary = _reportService.GetSummary(groups, filter);

        // 4 activities over 2 weeks = 2.0 per week
        Assert.Equal(2.0, summary.ActivitiesPerWeek);
    }

    [Fact]
    public void GetSummary_UsesFilterDatesWhenProvided()
    {
        var fromDate = new DateTime(2025, 1, 1);
        var toDate = new DateTime(2025, 1, 31);

        var groups = new List<JsaReportGroup>();
        var filter = new JsaReportFilter { FromDate = fromDate, ToDate = toDate };
        var summary = _reportService.GetSummary(groups, filter);

        Assert.Equal(fromDate, summary.DateFrom);
        Assert.Equal(toDate, summary.DateTo);
    }

    [Fact]
    public void GetSummary_InfersDateRangeFromEntries()
    {
        var date1 = new DateTime(2025, 1, 1);
        var date2 = new DateTime(2025, 1, 15);

        var groups = new List<JsaReportGroup>
        {
            new()
            {
                Entries = new()
                {
                    new() { Timestamp = date1 },
                    new() { Timestamp = date2 }
                }
            }
        };

        var filter = new JsaReportFilter();
        var summary = _reportService.GetSummary(groups, filter);

        Assert.Equal(date1.Date, summary.DateFrom);
        Assert.Equal(date2.Date, summary.DateTo);
    }

    [Fact]
    public void GetSummary_CreatesActionTypeCounts()
    {
        var groups = new List<JsaReportGroup>
        {
            new()
            {
                Entries = new()
                {
                    new() { ActionType = HistoryActionType.JobAdded },
                    new() { ActionType = HistoryActionType.JobAdded },
                    new() { ActionType = HistoryActionType.AppliedStatusChanged },
                    new() { ActionType = HistoryActionType.ContactDiscussion }
                }
            }
        };

        var filter = new JsaReportFilter();
        var summary = _reportService.GetSummary(groups, filter);

        Assert.Equal(2, summary.ActionTypeCounts[HistoryActionType.JobAdded]);
        Assert.Equal(1, summary.ActionTypeCounts[HistoryActionType.AppliedStatusChanged]);
        Assert.Equal(1, summary.ActionTypeCounts[HistoryActionType.ContactDiscussion]);
    }

    [Fact]
    public void GetActionTypeDisplay_ReturnsCorrectDisplayNames()
    {
        Assert.Equal("Job Added", JsaReportService.GetActionTypeDisplay(HistoryActionType.JobAdded));
        Assert.Equal("Applied", JsaReportService.GetActionTypeDisplay(HistoryActionType.AppliedStatusChanged));
        Assert.Equal("Stage Change", JsaReportService.GetActionTypeDisplay(HistoryActionType.ApplicationStageChanged));
        Assert.Equal("Interest", JsaReportService.GetActionTypeDisplay(HistoryActionType.InterestChanged));
        Assert.Equal("Suitability", JsaReportService.GetActionTypeDisplay(HistoryActionType.SuitabilityChanged));
        Assert.Equal("Contact/Discussion", JsaReportService.GetActionTypeDisplay(HistoryActionType.ContactDiscussion));
    }

    [Fact]
    public void DefaultJsaActionTypes_IncludesContactDiscussion()
    {
        Assert.Contains(HistoryActionType.ContactDiscussion, JsaReportService.DefaultJsaActionTypes);
    }

    [Fact]
    public void MultipleStandaloneEntries_EachGetsOwnGroup()
    {
        var entries = new List<JobHistoryEntry>
        {
            new()
            {
                JobId = Guid.Empty,
                JobTitle = "Developer",
                Company = "Acme",
                ActionType = HistoryActionType.ContactDiscussion,
                ContactName = "John",
                UserId = _userId,
                Timestamp = DateTime.Now
            },
            new()
            {
                JobId = Guid.Empty,
                JobTitle = "Designer",
                Company = "Beta",
                ActionType = HistoryActionType.ContactDiscussion,
                ContactName = "Jane",
                UserId = _userId,
                Timestamp = DateTime.Now.AddHours(-1)
            }
        };

        SetupHistoryEntries(entries);

        var filter = new JsaReportFilter();
        var groups = _reportService.GenerateReport(filter);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(Guid.Empty, g.JobId));
        Assert.All(groups, g => Assert.Single(g.Entries));
        Assert.Contains(groups, g => g.JobTitle == "Developer");
        Assert.Contains(groups, g => g.JobTitle == "Designer");
    }
}
