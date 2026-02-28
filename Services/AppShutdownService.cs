using Microsoft.Extensions.Hosting;

namespace JobTracker.Services;

public class AppShutdownService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<AppShutdownService> _logger;

    public AppShutdownService(
        IHostApplicationLifetime lifetime,
        ILogger<AppShutdownService> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public void Shutdown()
    {
        _logger.LogWarning("[Shutdown] User requested application shutdown");

        // Use a background thread so the Blazor component doesn't hang waiting
        // StopApplication signals the host to stop, which triggers orderly shutdown
        // of all hosted services (including LocalBackgroundService)
        Task.Run(() =>
        {
            _logger.LogWarning("[Shutdown] Calling StopApplication()");
            _lifetime.StopApplication();
        });
    }
}
