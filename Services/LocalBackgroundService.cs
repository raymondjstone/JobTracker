using System.Text.Json;
using JobTracker.Models;

namespace JobTracker.Services;

public class BackgroundJobStatus
{
    public string Name { get; set; } = "";
    public TimeSpan Interval { get; set; }
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }
    public TimeSpan? LastDuration { get; set; }
    public bool IsRunning { get; set; }
    public string? LastError { get; set; }
    public bool Enabled { get; set; } = true;
    public bool RunNowRequested { get; set; }
}

public class LocalBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalBackgroundService> _logger;
    private readonly string _configPath;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

    private static readonly Dictionary<string, (string Name, TimeSpan DefaultInterval)> JobDefaults = new()
    {
        ["AvailabilityCheck-NotChecked"] = ("Availability Check (NotChecked)", TimeSpan.FromHours(6)),
        ["AvailabilityCheck-Possible"] = ("Availability Check (Possible)", TimeSpan.FromHours(12)),
        ["GhostedCheck"] = ("Ghosted Check", TimeSpan.FromHours(24)),
        ["NoReplyCheck"] = ("No Reply Check", TimeSpan.FromHours(24)),
        ["JobCrawl"] = ("Job Crawl", TimeSpan.FromHours(48)),
    };

    private readonly Dictionary<string, BackgroundJobStatus> _jobStatuses = new();
    private DateTime? _startedAt;

    public LocalBackgroundService(IServiceScopeFactory scopeFactory, ILogger<LocalBackgroundService> logger, IWebHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configPath = Path.Combine(env.ContentRootPath, "Data", "background-jobs.json");

        // Initialize defaults (disabled until user enables them)
        var hasConfig = File.Exists(_configPath);
        foreach (var (key, (name, interval)) in JobDefaults)
        {
            _jobStatuses[key] = new BackgroundJobStatus
            {
                Name = name,
                Interval = interval,
                Enabled = false
            };
        }

        // Merge saved config if it exists
        if (hasConfig)
            LoadConfig();
        else
            SaveConfig();
    }

    public IReadOnlyDictionary<string, BackgroundJobStatus> JobStatuses => _jobStatuses;
    public DateTime? StartedAt => _startedAt;

    public void SetEnabled(string key, bool enabled)
    {
        if (_jobStatuses.TryGetValue(key, out var status))
        {
            status.Enabled = enabled;
            if (enabled)
            {
                status.NextRun = DateTime.Now.Add(status.Interval);
                status.RunNowRequested = true; // wake the loop
            }
            else
            {
                status.NextRun = null;
            }
            SaveConfig();
        }
    }

    public void SetInterval(string key, TimeSpan interval)
    {
        if (interval < TimeSpan.FromMinutes(1))
            interval = TimeSpan.FromMinutes(1);

        if (_jobStatuses.TryGetValue(key, out var status))
        {
            status.Interval = interval;
            if (status.Enabled && status.LastRun.HasValue)
                status.NextRun = status.LastRun.Value.Add(interval);
            else if (status.Enabled)
                status.NextRun = DateTime.Now.Add(interval);
            SaveConfig();
        }
    }

    public void RunNow(string key)
    {
        if (_jobStatuses.TryGetValue(key, out var status))
        {
            status.RunNowRequested = true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Background] LocalBackgroundService starting, initial delay of {Delay}", InitialDelay);
        _startedAt = DateTime.Now;

        var tasks = new[]
        {
            RunLoop("AvailabilityCheck-NotChecked", RunAvailabilityCheckNotChecked, stoppingToken),
            RunLoop("AvailabilityCheck-Possible", RunAvailabilityCheckPossible, stoppingToken),
            RunLoop("GhostedCheck", RunGhostedCheck, stoppingToken),
            RunLoop("NoReplyCheck", RunNoReplyCheck, stoppingToken),
            RunLoop("JobCrawl", RunJobCrawl, stoppingToken),
        };

        await Task.WhenAll(tasks);
    }

    private async Task RunLoop(string key, Func<Task> job, CancellationToken ct)
    {
        var status = _jobStatuses[key];

        // Initial delay — can be skipped by RunNow
        var initialEnd = DateTime.Now.Add(InitialDelay);
        while (DateTime.Now < initialEnd && !ct.IsCancellationRequested)
        {
            if (status.RunNowRequested)
                break;
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        if (ct.IsCancellationRequested) return;

        // Run on first iteration if enabled or RunNow was requested
        if (status.Enabled || status.RunNowRequested)
        {
            status.RunNowRequested = false;
            await RunSafe(key, job);
        }
        else
        {
            status.NextRun = null;
        }

        while (!ct.IsCancellationRequested)
        {
            // Sleep in short increments so we can react to RunNow quickly
            var sleepUntil = DateTime.Now.Add(status.Interval);
            if (status.Enabled)
                status.NextRun = sleepUntil;

            while (DateTime.Now < sleepUntil && !ct.IsCancellationRequested)
            {
                if (status.RunNowRequested)
                    break;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }

            if (ct.IsCancellationRequested)
                break;

            var runNow = status.RunNowRequested;
            status.RunNowRequested = false;

            if (status.Enabled || runNow)
            {
                await RunSafe(key, job);
            }
            else
            {
                status.NextRun = null;
            }
        }
    }

    private async Task RunSafe(string key, Func<Task> job)
    {
        var status = _jobStatuses[key];
        status.IsRunning = true;
        status.LastError = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("[Background] Running {Job}", key);
            await job();
            sw.Stop();
            status.LastRun = DateTime.Now;
            status.LastDuration = sw.Elapsed;
            status.NextRun = status.Enabled ? DateTime.Now.Add(status.Interval) : null;
            _logger.LogInformation("[Background] Completed {Job} in {Duration}", key, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            status.LastError = ex.Message;
            status.LastDuration = sw.Elapsed;
            status.NextRun = status.Enabled ? DateTime.Now.Add(status.Interval) : null;
            _logger.LogError(ex, "[Background] Error running {Job}", key);
        }
        finally
        {
            status.IsRunning = false;
        }
    }

    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(_configPath)) return;

            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<Dictionary<string, JobConfig>>(json);
            if (config == null) return;

            foreach (var (key, cfg) in config)
            {
                if (_jobStatuses.TryGetValue(key, out var status))
                {
                    status.Enabled = cfg.Enabled;
                    if (cfg.IntervalHours > 0)
                        status.Interval = TimeSpan.FromHours(cfg.IntervalHours);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load background job config");
        }
    }

    private void SaveConfig()
    {
        try
        {
            var config = _jobStatuses.ToDictionary(
                kvp => kvp.Key,
                kvp => new JobConfig
                {
                    Enabled = kvp.Value.Enabled,
                    IntervalHours = kvp.Value.Interval.TotalHours
                });

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save background job config");
        }
    }

    private record JobConfig
    {
        public bool Enabled { get; init; } = true;
        public double IntervalHours { get; init; }
    }

    private async Task RunAvailabilityCheckNotChecked()
    {
        using var scope = _scopeFactory.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<AvailabilityCheckJob>();
        await job.RunAsync(SuitabilityStatus.NotChecked);
    }

    private async Task RunAvailabilityCheckPossible()
    {
        using var scope = _scopeFactory.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<AvailabilityCheckJob>();
        await job.RunAsync(SuitabilityStatus.Possible);
    }

    private Task RunGhostedCheck()
    {
        using var scope = _scopeFactory.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<GhostedCheckJob>();
        job.Run();
        return Task.CompletedTask;
    }
    private Task RunNoReplyCheck()
    {
        using var scope = _scopeFactory.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<NoReplyCheckJob>();
        job.Run();
        return Task.CompletedTask;
    }

    private async Task RunJobCrawl()
    {
        using var scope = _scopeFactory.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<JobCrawlJob>();
        await job.RunAsync();
    }
}
