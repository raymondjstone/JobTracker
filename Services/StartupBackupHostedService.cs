namespace JobTracker.Services;

public class StartupBackupHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StartupBackupHostedService> _logger;

    public StartupBackupHostedService(IServiceScopeFactory scopeFactory, ILogger<StartupBackupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    private void RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var appSettingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
            var settings = appSettingsService.GetSettings();

            if (!settings.BackupOnStartup)
                return;

            var job = scope.ServiceProvider.GetRequiredService<ScheduledBackupJob>();
            job.Run();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[StartupBackup] Failed to create startup backup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
