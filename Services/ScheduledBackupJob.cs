using System.IO.Compression;

namespace JobTracker.Services;

public class ScheduledBackupJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledBackupJob> _logger;

    public ScheduledBackupJob(IServiceScopeFactory scopeFactory, ILogger<ScheduledBackupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Run()
    {
        _logger.LogInformation("[Background] Starting scheduled backup");

        using var scope = _scopeFactory.CreateScope();
        var storageBackend = scope.ServiceProvider.GetRequiredService<IStorageBackend>();

        if (storageBackend is not JsonStorageBackend jsonBackend)
        {
            _logger.LogInformation("[Background] Scheduled backup skipped: not using JSON storage");
            return;
        }

        var appSettingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
        var settings = appSettingsService.GetSettings();

        var dataDir = jsonBackend.GetDataDirectory();
        var backupDir = string.IsNullOrWhiteSpace(settings.BackupDirectory)
            ? Path.Combine(dataDir, "Backups")
            : settings.BackupDirectory;
        Directory.CreateDirectory(backupDir);

        var jsonFiles = Directory.GetFiles(dataDir, "*.json");
        if (jsonFiles.Length == 0)
        {
            _logger.LogInformation("[Background] Scheduled backup skipped: no JSON files found");
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        var zipPath = Path.Combine(backupDir, $"jobtracker-backup-{timestamp}.zip");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            foreach (var file in jsonFiles)
            {
                archive.CreateEntryFromFile(file, Path.GetFileName(file));
            }
        }

        _logger.LogInformation("[Background] Scheduled backup created: {ZipPath} ({FileCount} files)", zipPath, jsonFiles.Length);

        // Prune old backups, keep most recent 10
        var existingBackups = Directory.GetFiles(backupDir, "jobtracker-backup-*.zip")
            .OrderByDescending(f => f)
            .ToList();

        if (existingBackups.Count > 10)
        {
            var toDelete = existingBackups.Skip(10).ToList();
            foreach (var old in toDelete)
            {
                try
                {
                    File.Delete(old);
                    _logger.LogInformation("[Background] Pruned old backup: {File}", Path.GetFileName(old));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Background] Failed to delete old backup: {File}", old);
                }
            }
        }

        _logger.LogInformation("[Background] Scheduled backup complete: {FileCount} files backed up, {BackupCount} backups retained",
            jsonFiles.Length, Math.Min(existingBackups.Count, 10));
    }
}
