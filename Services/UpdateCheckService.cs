using System.Reflection;
using System.Text.Json;

namespace JobTracker.Services;

public class UpdateCheckService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdateCheckService> _logger;

    private string? _latestVersion;
    private string? _downloadUrl;
    private bool _checked;

    public string CurrentVersion { get; }
    public string? LatestVersion => _latestVersion;
    public string? DownloadUrl => _downloadUrl;
    public bool UpdateAvailable => _checked && _latestVersion != null
        && CompareVersions(_latestVersion, CurrentVersion) > 0;

    public UpdateCheckService(
        IHttpClientFactory httpClientFactory,
        ILogger<UpdateCheckService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        CurrentVersion = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]   // strip +commit hash suffix
            ?? "0.0.0.0";
    }

    /// <summary>
    /// Check GitHub releases for a newer version. Safe to call multiple times;
    /// it will only hit the network once per app lifetime.
    /// </summary>
    public async Task CheckForUpdateAsync()
    {
        if (_checked) return;
        _checked = true;

        try
        {
            var client = _httpClientFactory.CreateClient("UpdateCheck");
            var response = await client.GetAsync(
                "https://api.github.com/repos/raymondjstone/JobTracker/releases/latest");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub release check returned {Status}", response.StatusCode);
                return;
            }

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync());

            var root = doc.RootElement;
            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = tagName.TrimStart('v');

            _downloadUrl = root.GetProperty("html_url").GetString();
            _latestVersion = latestVersion;

            _logger.LogInformation("Version check: current={Current}, latest={Latest}, updateAvailable={Available}",
                CurrentVersion, _latestVersion, UpdateAvailable);

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates");
        }
    }

    /// <summary>
    /// Compare two dotted version strings (e.g. "1.26.0406.42" vs "1.26.0405.10").
    /// All segments are compared. The 4th segment is a CI run number in releases
    /// (small incrementing int) but HHmm in local dev builds (900-2359), so local
    /// dev builds will always appear newer than releases — this is acceptable since
    /// devs don't need the update banner.
    /// </summary>
    private static int CompareVersions(string a, string b)
    {
        var partsA = a.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var partsB = b.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();

        var len = Math.Max(partsA.Length, partsB.Length);
        for (int i = 0; i < len; i++)
        {
            var pa = i < partsA.Length ? partsA[i] : 0;
            var pb = i < partsB.Length ? partsB[i] : 0;
            if (pa != pb) return pa - pb;
        }
        return 0;
    }
}
