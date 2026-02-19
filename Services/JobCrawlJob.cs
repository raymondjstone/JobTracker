namespace JobTracker.Services;

public class JobCrawlJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobCrawlJob> _logger;

    public JobCrawlJob(IServiceScopeFactory scopeFactory, ILogger<JobCrawlJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("[Hangfire] Starting job crawl");

        using var scope = _scopeFactory.CreateScope();
        var crawlService = scope.ServiceProvider.GetRequiredService<JobCrawlService>();
        var jobService = scope.ServiceProvider.GetRequiredService<JobListingService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();

        var users = authService.GetAllUsers();

        foreach (var user in users)
        {
            try
            {
                var settings = settingsService.GetSettings(user.Id);
                var siteUrls = settings.JobSiteUrls;

                _logger.LogInformation("[Hangfire] Crawling jobs for user {User}", user.Email);
                var result = await crawlService.CrawlAllSitesAsync(siteUrls, user.Id, jobService);

                _logger.LogInformation(
                    "[Hangfire] Job crawl complete for user {User}: {Found} found, {Added} added, {Pages} pages, {Errors} errors",
                    user.Email, result.JobsFound, result.JobsAdded, result.PagesScanned, result.Errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Hangfire] Error crawling jobs for user {User}", user.Email);
            }
        }
    }
}
