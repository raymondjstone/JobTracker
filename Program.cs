using Hangfire;
using Hangfire.Dashboard;
using JobTracker.Components;
using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// One-off migration: SQL Server -> Local JSON storage
if (args.Contains("--migrate-to-local"))
{
    await MigrateToLocal(builder);
    return;
}

// Read LocalMode setting early
var localMode = builder.Configuration.GetValue<bool>("LocalMode");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add HttpContextAccessor for auth pages
builder.Services.AddHttpContextAccessor();

// Configure authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Configure storage backend — LocalMode forces Json
var storageProvider = localMode ? "Json" : (builder.Configuration.GetValue<string>("StorageProvider") ?? "Json");

if (string.Equals(storageProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContextFactory<JobSearchDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("JobSearchDb")));
    builder.Services.AddSingleton<IStorageBackend, SqlServerStorageBackend>();
}
else
{
    builder.Services.AddSingleton<IStorageBackend>(sp =>
    {
        var env = sp.GetRequiredService<IWebHostEnvironment>();
        var logger = sp.GetRequiredService<ILogger<JsonStorageBackend>>();
        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        return new JsonStorageBackend(dataDir, logger);
    });
}

// Auth services
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<EmailService>();

// Current user context (scoped to request)
builder.Services.AddScoped<CurrentUserService>();

// Application services
builder.Services.AddScoped<JobListingService>();
builder.Services.AddScoped<AppSettingsService>();
builder.Services.AddScoped<JobRulesService>();
builder.Services.AddScoped<JobHistoryService>();

// Register Lazy<T> for services that have circular dependencies
builder.Services.AddScoped(sp => new Lazy<JobRulesService>(() => sp.GetRequiredService<JobRulesService>()));
builder.Services.AddScoped(sp => new Lazy<JobHistoryService>(() => sp.GetRequiredService<JobHistoryService>()));

// Configure HttpClient for LinkedIn job extraction
builder.Services.AddHttpClient<LinkedInJobExtractor>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
});

// Configure HttpClient for job availability checking
builder.Services.AddHttpClient<JobAvailabilityService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
});

// Configure HttpClient for job crawling
builder.Services.AddHttpClient<JobCrawlService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-GB,en;q=0.9");
    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
});

// Add Hangfire (only for SQL Server mode, not LocalMode)
if (!localMode && string.Equals(storageProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(builder.Configuration.GetConnectionString("JobSearchDb")));
    builder.Services.AddHangfireServer();
}

builder.Services.AddTransient<AvailabilityCheckJob>();
builder.Services.AddTransient<JobCrawlJob>();
builder.Services.AddTransient<GhostedCheckJob>();
builder.Services.AddTransient<NoReplyCheckJob>();

// In LocalMode, use a BackgroundService for recurring jobs
if (localMode)
{
    builder.Services.AddHostedService<LocalBackgroundService>();
}

// Add CORS for browser extension — restricted to known origins
// Content scripts run in the context of web pages, so their Origin is the page's domain.
// API endpoints are protected by API key auth, so CORS is secondary protection here.
builder.Services.AddCors(options =>
{
    options.AddPolicy("Extensions", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            {
                if (origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                    origin.StartsWith("edge-extension://", StringComparison.OrdinalIgnoreCase))
                    return true;

                // Allow localhost (dev)
                if (Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                    uri.Host == "localhost")
                    return true;

                // Allow job site domains where content scripts run
                var allowedDomains = new[] {
                    "linkedin.com", "indeed.com", "s1jobs.com",
                    "welcometothejungle.com", "energyjobsearch.com"
                };
                if (Uri.TryCreate(origin, UriKind.Absolute, out var siteUri))
                {
                    var host = siteUri.Host;
                    return allowedDomains.Any(d =>
                        host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                        host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
                }

                return false;
            })
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// If using SQL Server, apply any pending migrations
if (!localMode && string.Equals(storageProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<JobSearchDbContext>>();
    using (var db = dbFactory.CreateDbContext())
    {
        try
        {
            db.Database.Migrate();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Message.Contains("already exists"))
        {
            // Database or tables already exist - skip migration errors
            var dbLogger = app.Services.GetRequiredService<ILogger<Program>>();
            dbLogger.LogInformation("Database already exists, skipping migration: {Message}", ex.Message);
        }
    }
}

// Seed initial user if no users exist
var storage = app.Services.GetRequiredService<IStorageBackend>();
var authService = app.Services.GetRequiredService<AuthService>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

if (!authService.GetAllUsers().Any())
{
    if (localMode)
    {
        var localUser = authService.CreateUser(
            email: "local@localhost",
            name: "Local User",
            password: Guid.NewGuid().ToString()
        );
        startupLogger.LogInformation("Created local user: {Email}", localUser.Email);
        storage.MigrateExistingDataToUser(localUser.Id);
        startupLogger.LogInformation("Migrated existing data to local user");
    }
    else
    {
        var initialPassword = builder.Configuration["InitialUser:Password"]
            ?? throw new InvalidOperationException("InitialUser:Password must be configured in appsettings.json or environment variables.");
        var initialEmail = builder.Configuration["InitialUser:Email"] ?? "admin@localhost";
        var initialName = builder.Configuration["InitialUser:Name"] ?? "Admin";
        var initialUser = authService.CreateUser(
            email: initialEmail,
            name: initialName,
            password: initialPassword
        );
        startupLogger.LogInformation("Created initial user: {Email}", initialUser.Email);
        storage.MigrateExistingDataToUser(initialUser.Id);
        startupLogger.LogInformation("Migrated existing data to user: {Email}", initialUser.Email);
    }
}

// Backfill ApiKey for existing users that don't have one persisted yet.
// (The property initializer generates a random key on deserialization, but it's
// not stable until explicitly saved. This ensures it's persisted once.)
foreach (var existingUser in authService.GetAllUsers())
{
    // ApiKey will have been set by the property initializer — save it so it persists
    if (!string.IsNullOrEmpty(existingUser.ApiKey))
    {
        storage.SaveUser(existingUser);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// CORS for browser extensions (only the "Extensions" named policy is defined)
app.UseCors("Extensions");

// LocalMode: auto-authenticate all requests
if (localMode)
{
    app.UseMiddleware<LocalAuthMiddleware>();
}

// Authentication and Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Helper to get user ID from request (API key or cookie auth)
Guid? GetUserIdFromRequest(HttpContext context, AuthService authService)
{
    // In LocalMode, always return the local user — no API key needed
    if (localMode)
    {
        var localUser = authService.GetAllUsers().FirstOrDefault();
        if (localUser != null) return localUser.Id;
    }

    // First check for API key header (for browser extension)
    var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
    if (!string.IsNullOrEmpty(apiKey))
    {
        var user = authService.ValidateApiKey(apiKey);
        if (user != null) return user.Id;
    }

    // Fall back to cookie auth
    return AuthService.GetUserId(context.User);
}

// API endpoints BEFORE antiforgery (they use their own CORS/auth)
app.MapPost("/api/jobs", async (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    Console.WriteLine($"[API] POST /api/jobs - Content-Type: {context.Request.ContentType}");

    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID");
        return Results.Unauthorized();
    }

    try
    {
        var job = await context.Request.ReadFromJsonAsync<JobListing>();
        if (job == null)
        {
            Console.WriteLine("[API] Error: job is null");
            return Results.BadRequest("Invalid job data");
        }

        // Input validation
        if (string.IsNullOrWhiteSpace(job.Title))
            return Results.BadRequest("Title is required.");
        if (!string.IsNullOrWhiteSpace(job.Url) && !Uri.TryCreate(job.Url, UriKind.Absolute, out _))
            return Results.BadRequest("Invalid URL format.");
        if (job.Title.Length > 500)
            return Results.BadRequest("Title exceeds maximum length.");
        if (job.Company?.Length > 500)
            return Results.BadRequest("Company exceeds maximum length.");
        if (job.Description?.Length > 50000)
            return Results.BadRequest("Description exceeds maximum length.");

        job.UserId = userId.Value;
        var wasAdded = jobService.AddJobListing(job);

        if (wasAdded)
        {
            Console.WriteLine($"[API] Added job: {job.Title} at {job.Company}");
            return Results.Created($"/api/jobs/{job.Id}", new { added = true, job });
        }
        else
        {
            Console.WriteLine($"[API] Duplicate skipped: {job.Title} at {job.Company}");
            return Results.Ok(new { added = false, message = "Duplicate job - already exists" });
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[API] Error: {ex.Message}");
        return Results.BadRequest("An error occurred processing your request.");
    }
}).DisableAntiforgery();

app.MapGet("/api/jobs", (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for GET /api/jobs");
        return Results.Unauthorized();
    }

    var jobs = jobService.GetAllJobListings(userId.Value);
    Console.WriteLine($"[API] Returning {jobs.Count} jobs for user {userId}");
    return Results.Ok(jobs);
});

app.MapGet("/api/jobs/count", (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for GET /api/jobs/count");
        return Results.Unauthorized();
    }

    return Results.Ok(new { count = jobService.GetTotalCount(userId.Value) });
});

app.MapGet("/api/jobs/stats", (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for GET /api/jobs/stats");
        return Results.Unauthorized();
    }

    var stats = jobService.GetStats(userId.Value);
    return Results.Ok(stats);
});

app.MapGet("/api/jobs/exists", (string url, HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for GET /api/jobs/exists");
        return Results.Unauthorized();
    }

    var exists = jobService.JobExists(url, userId.Value);
    return Results.Ok(new { exists });
});

app.MapPut("/api/jobs/{id:guid}/interest", async (Guid id, HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        return Results.Unauthorized();
    }

    try
    {
        var body = await context.Request.ReadFromJsonAsync<InterestUpdateRequest>();
        if (body == null)
        {
            return Results.BadRequest("Invalid request");
        }

        var job = jobService.GetJobListingById(id, userId.Value);
        if (job == null || job.UserId != userId.Value)
        {
            return Results.NotFound();
        }

        jobService.SetInterestStatus(id, body.Interest);
        Console.WriteLine($"[API] Updated interest for {job.Title}: {body.Interest}");
        return Results.Ok(new { success = true, interest = body.Interest });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[API] Error: {ex.Message}");
        return Results.BadRequest("An error occurred processing your request.");
    }
}).DisableAntiforgery();

app.MapPost("/api/jobs/cleanup", (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for cleanup");
        return Results.Unauthorized();
    }

    var cleanedCount = jobService.CleanupAllJobs(userId.Value);
    Console.WriteLine($"[API] Cleaned up {cleanedCount} jobs");
    return Results.Ok(new { cleaned = cleanedCount, message = $"Cleaned up {cleanedCount} job titles" });
}).DisableAntiforgery();

// Clean LinkedIn boilerplate from all job descriptions
app.MapPost("/api/jobs/clean-descriptions", (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for clean-descriptions");
        return Results.Unauthorized();
    }

    var cleanedCount = jobService.CleanAllDescriptions(userId.Value);
    Console.WriteLine($"[API] Cleaned {cleanedCount} job descriptions");
    return Results.Ok(new { cleaned = cleanedCount, message = $"Cleaned boilerplate from {cleanedCount} job descriptions" });
}).DisableAntiforgery();

app.MapDelete("/api/jobs/{id:guid}", (Guid id, HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for DELETE job");
        return Results.Unauthorized();
    }

    var job = jobService.GetJobListingById(id, userId.Value);
    if (job == null || job.UserId != userId.Value)
    {
        return Results.NotFound();
    }
    jobService.DeleteJobListing(job.Id);
    return Results.NoContent();
}).DisableAntiforgery();

app.MapDelete("/api/jobs/clear", (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for clear");
        return Results.Unauthorized();
    }

    jobService.ClearAllJobListings(userId.Value);
    Console.WriteLine("[API] All jobs cleared");
    return Results.Ok(new { message = "All jobs cleared" });
}).DisableAntiforgery();

// Update job description by URL
app.MapPut("/api/jobs/description", async (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for description update");
        return Results.Unauthorized();
    }

    try
    {
        var body = await context.Request.ReadFromJsonAsync<DescriptionUpdateRequest>();
        if (body == null || string.IsNullOrEmpty(body.Url))
        {
            return Results.BadRequest("Invalid request - URL required");
        }

        var updated = jobService.UpdateJobDescription(body.Url, body.Description ?? "", userId.Value, body.Company);
        if (updated)
        {
            Console.WriteLine($"[API] Updated description for URL: {body.Url.Substring(0, Math.Min(50, body.Url.Length))}");
            return Results.Ok(new { updated = true });
        }
        else
        {
            Console.WriteLine($"[API] Description not updated for URL: {body.Url.Substring(0, Math.Min(50, body.Url.Length))} (job not found or description unchanged)");
            return Results.Ok(new { updated = false, message = "Job not found or description unchanged" });
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[API] Error: {ex.Message}");
        return Results.BadRequest("An error occurred processing your request.");
    }
}).DisableAntiforgery();

// Get jobs that need descriptions - returns URLs for the extension to fetch
app.MapGet("/api/jobs/needing-descriptions", (int? limit, HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for needing-descriptions");
        return Results.Unauthorized();
    }

    Console.WriteLine($"[API] GET /api/jobs/needing-descriptions - User ID: {userId}");
    
    // Pass the userId explicitly to ensure we get jobs for the API-authenticated user
    var jobs = jobService.GetJobsNeedingDescriptions(limit ?? int.MaxValue, userId.Value);
    
    Console.WriteLine($"[API] Found {jobs.Count} jobs needing descriptions:");
    foreach (var job in jobs.Take(5))
    {
        Console.WriteLine($"  - {job.Title} ({job.Source}) - Desc length: {job.Description?.Length ?? 0}");
    }
    
    var urls = jobs.Select(j => new { j.Url, j.Title, j.Company, j.Source }).ToList();
    Console.WriteLine($"[API] Returning {urls.Count} jobs needing descriptions for user {userId}");
    return Results.Ok(new { count = urls.Count, jobs = urls });
});

// Debug endpoint to help diagnose data issues
app.MapGet("/api/jobs/debug", (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        return Results.Unauthorized();
    }

    var allJobs = jobService.GetAllJobListings(userId.Value);
    var stats = jobService.GetStats(userId.Value);
    
    return Results.Ok(new
    {
        userId = userId.Value,
        totalJobs = allJobs.Count,
        stats = stats,
        sampleJobs = allJobs.Take(3).Select(j => new
        {
            j.Title,
            j.Company,
            j.Source,
            descLength = j.Description?.Length ?? 0,
            j.Suitability,
            j.UserId
        })
    });
});

// API endpoint to remove duplicate jobs
app.MapPost("/api/jobs/remove-duplicates", (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for remove-duplicates");
        return Results.Unauthorized();
    }

    var removed = jobService.RemoveDuplicateJobs(userId.Value);
    Console.WriteLine($"[API] Removed {removed} duplicate jobs");
    return Results.Ok(new { removed });
}).DisableAntiforgery();

// API endpoint to fix unknown sources from URLs
app.MapPost("/api/jobs/fix-sources", (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
    {
        Console.WriteLine("[API] Unauthorized - no valid user ID for fix-sources");
        return Results.Unauthorized();
    }

    var count = jobService.FixUnknownSources(userId.Value);
    Console.WriteLine($"[API] Fixed sources for {count} jobs");
    return Results.Ok(new { fixedCount = count, message = $"Fixed sources for {count} jobs" });
}).DisableAntiforgery();

// API endpoint to get jobs needing availability checks (for browser-side checking)
app.MapGet("/api/jobs/needing-availability-check", (string? source, HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
        return Results.Unauthorized();

    var cutoff = DateTime.Now.AddHours(-4);
    var allJobs = jobService.GetAllJobListings(userId);
    var query = allJobs
        .Where(j => !j.HasApplied
            && (j.Suitability == SuitabilityStatus.NotChecked || j.Suitability == SuitabilityStatus.Possible))
        .Where(j => !string.IsNullOrWhiteSpace(j.Url))
        .Where(j => !j.LastChecked.HasValue || j.LastChecked.Value < cutoff);

    if (!string.IsNullOrWhiteSpace(source))
        query = query.Where(j => string.Equals(j.Source, source, StringComparison.OrdinalIgnoreCase));

    var jobs = query
        .OrderBy(j => j.LastChecked.HasValue ? 1 : 0)
        .ThenBy(j => j.LastChecked ?? DateTime.MinValue)
        .Select(j => new { j.Id, j.Url, j.Title, j.Company, j.Source })
        .ToList();

    return Results.Ok(new { count = jobs.Count, jobs });
}).DisableAntiforgery();

// API endpoint for browser extensions to mark a job as unavailable
app.MapPost("/api/jobs/{id:guid}/mark-unavailable", async (Guid id, HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
        return Results.Unauthorized();

    var body = await context.Request.ReadFromJsonAsync<MarkUnavailableRequest>();
    var reason = body?.Reason ?? "Job no longer available";

    jobService.SetSuitabilityStatus(id, SuitabilityStatus.Unsuitable, HistoryChangeSource.System, reason, userId);
    jobService.SetLastChecked(id, userId);
    Console.WriteLine($"[API] Job {id} marked unavailable: {reason}");
    return Results.Ok(new { success = true });
}).DisableAntiforgery();

// API endpoint for browser extensions to mark a job as unavailable by URL
app.MapPost("/api/jobs/mark-unavailable", async (HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
        return Results.Unauthorized();

    var body = await context.Request.ReadFromJsonAsync<MarkUnavailableByUrlRequest>();
    if (body == null || string.IsNullOrWhiteSpace(body.Url))
        return Results.BadRequest("URL required");

    var job = jobService.FindJobByUrl(body.Url, userId.Value);
    if (job == null)
        return Results.Ok(new { success = false, message = "Job not found" });

    var reason = body.Reason ?? "Job no longer available";
    jobService.SetSuitabilityStatus(job.Id, SuitabilityStatus.Unsuitable, HistoryChangeSource.System, reason, userId);
    jobService.SetLastChecked(job.Id, userId);
    Console.WriteLine($"[API] Job marked unavailable by URL: {job.Title} - {reason}");
    return Results.Ok(new { success = true });
}).DisableAntiforgery();

// API endpoint for browser extensions to mark a job as checked (available)
app.MapPost("/api/jobs/{id:guid}/mark-checked", (Guid id, HttpContext context, JobListingService jobService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
        return Results.Unauthorized();

    jobService.SetLastChecked(id, userId);
    Console.WriteLine($"[API] Job {id} marked as checked (still available)");
    return Results.Ok(new { success = true });
}).DisableAntiforgery();

// API endpoint for browser extensions to check job availability (server-side)
app.MapPost("/api/jobs/check-availability", async (HttpContext context, JobListingService jobService, JobAvailabilityService availabilityService, JobHistoryService historyService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
        return Results.Unauthorized();

    var body = await context.Request.ReadFromJsonAsync<CheckAvailabilityRequest>();
    if (body == null || string.IsNullOrWhiteSpace(body.Source))
        return Results.BadRequest(new { error = "source is required" });

    var cutoff = DateTime.Now.AddHours(-4);
    var allJobs = jobService.GetAllJobListings(userId);
    var jobsToCheck = allJobs
        .Where(j => !j.HasApplied
            && (j.Suitability == SuitabilityStatus.NotChecked || j.Suitability == SuitabilityStatus.Possible))
        .Where(j => string.Equals(j.Source, body.Source, StringComparison.OrdinalIgnoreCase))
        .Where(j => !string.IsNullOrWhiteSpace(j.Url))
        .Where(j => !j.LastChecked.HasValue || j.LastChecked.Value < cutoff)
        .OrderBy(j => j.LastChecked.HasValue ? 1 : 0)
        .ThenBy(j => j.LastChecked ?? DateTime.MinValue)
        .ToList();

    if (jobsToCheck.Count == 0)
        return Results.Ok(new { total = 0, @checked = 0, markedUnavailable = 0, errors = 0 });

    int markedUnavailable = 0;
    int errors = 0;
    int skipped = 0;

    await availabilityService.ScanJobsAsync(
        jobsToCheck,
        markUnsuitableAction: (jobId, reason) =>
        {
            jobService.SetSuitabilityStatus(jobId, SuitabilityStatus.Unsuitable, HistoryChangeSource.System, reason);
            Interlocked.Increment(ref markedUnavailable);
        },
        markCheckedAction: (jobId) =>
        {
            jobService.SetLastChecked(jobId);
        },
        updateJobAction: (jobId, parsed) =>
        {
            jobService.UpdateJobIfBetter(jobId, parsed);
        },
        onProgress: (progress) =>
        {
            errors = progress.ErrorCount;
            skipped = progress.SkippedCount;
        }
    );

    Console.WriteLine($"[API] Availability check for {body.Source}: {jobsToCheck.Count} jobs, {markedUnavailable} unavailable, {errors} errors, {skipped} skipped");
    return Results.Ok(new { total = jobsToCheck.Count, @checked = jobsToCheck.Count - skipped, markedUnavailable, errors, skipped });
}).DisableAntiforgery();

// API endpoint to crawl job sites for new listings
app.MapPost("/api/jobs/crawl", async (HttpContext context, JobCrawlService crawlService, JobListingService jobService, AppSettingsService settingsService, AuthService authService) =>
{
    var userId = GetUserIdFromRequest(context, authService);
    if (!userId.HasValue)
        return Results.Unauthorized();

    var settings = settingsService.GetSettings(userId);
    var siteUrls = settings.JobSiteUrls;

    Console.WriteLine($"[API] Starting job crawl for user {userId}");
    var result = await crawlService.CrawlAllSitesAsync(siteUrls, userId.Value, jobService);
    Console.WriteLine($"[API] Crawl complete: {result.JobsFound} found, {result.JobsAdded} added, {result.PagesScanned} pages");

    return Results.Ok(result);
}).DisableAntiforgery();

// Antiforgery for Blazor pages only
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Configure Hangfire dashboard and recurring jobs
if (!localMode && string.Equals(storageProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter() }
    });

    RecurringJob.AddOrUpdate<AvailabilityCheckJob>(
        "availability-check-browse",
        job => job.RunAsync(SuitabilityStatus.NotChecked),
        "0 1 * * *"); // 1:00 AM daily

    RecurringJob.AddOrUpdate<AvailabilityCheckJob>(
        "availability-check-possible",
        job => job.RunAsync(SuitabilityStatus.Possible),
        "0 3 * * *"); // 3:00 AM daily

    RecurringJob.AddOrUpdate<GhostedCheckJob>(
        "ghosted-check",
        job => job.Run(),
        "0 5 * * *"); // 5:00 AM daily

    RecurringJob.AddOrUpdate<NoReplyCheckJob>(
        "noreply-check",
        job => job.Run(),
        "0 8 * * *"); // 8:00 AM daily

    RecurringJob.AddOrUpdate<JobCrawlJob>(
        "job-crawl",
        job => job.RunAsync(),
        "0 7 * * *"); // 7:00 AM daily
}

app.Run();

// One-off migration from SQL Server to local JSON storage
async Task MigrateToLocal(WebApplicationBuilder b)
{
    var logger = LoggerFactory.Create(l => l.AddConsole()).CreateLogger("Migration");

    var connString = b.Configuration.GetConnectionString("JobSearchDb");
    if (string.IsNullOrEmpty(connString))
    {
        logger.LogError("No ConnectionStrings:JobSearchDb found in config. Cannot migrate.");
        return;
    }

    // Set up SQL Server DbContext
    var optionsBuilder = new DbContextOptionsBuilder<JobSearchDbContext>();
    optionsBuilder.UseSqlServer(connString);
    using var db = new JobSearchDbContext(optionsBuilder.Options);

    // Set up JSON storage target
    var dataDir = Path.Combine(b.Environment.ContentRootPath, "Data");
    var jsonLogger = LoggerFactory.Create(l => l.AddConsole()).CreateLogger<JsonStorageBackend>();
    var jsonStorage = new JsonStorageBackend(dataDir, jsonLogger);

    // Read all users from SQL
    var users = db.Users.AsNoTracking().ToList();
    logger.LogInformation("Found {Count} users in SQL Server", users.Count);

    // Create a local user for LocalMode
    var localUserId = Guid.NewGuid();
    var sourceUser = users.FirstOrDefault();
    if (sourceUser == null)
    {
        logger.LogError("No users found in SQL Server. Nothing to migrate.");
        return;
    }

    // Create local user in JSON storage
    var localUser = new User
    {
        Id = localUserId,
        Email = "local@localhost",
        Name = "Local User",
        PasswordHash = sourceUser.PasswordHash,
        CreatedAt = DateTime.Now
    };
    jsonStorage.AddUser(localUser);
    logger.LogInformation("Created local user: {Email} ({Id})", localUser.Email, localUser.Id);

    // Migrate all jobs in bulk
    var allJobs = new List<JobListing>();
    foreach (var user in users)
    {
        var jobs = db.JobListings.AsNoTracking().Where(j => j.UserId == user.Id).ToList();
        foreach (var job in jobs)
        {
            job.UserId = localUserId;
        }
        allJobs.AddRange(jobs);
        logger.LogInformation("Read {Count} jobs from user {Email}", jobs.Count, user.Email);
    }
    jsonStorage.SaveJobs(allJobs, localUserId);
    logger.LogInformation("Total jobs migrated: {Count}", allJobs.Count);

    // Migrate all history in bulk
    var allHistory = new List<JobHistoryEntry>();
    foreach (var user in users)
    {
        var history = db.HistoryEntries.AsNoTracking()
            .Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.Timestamp)
            .ToList();
        foreach (var entry in history)
        {
            entry.UserId = localUserId;
        }
        allHistory.AddRange(history);
        logger.LogInformation("Read {Count} history entries from user {Email}", history.Count, user.Email);
    }
    jsonStorage.SaveHistory(allHistory, localUserId);
    logger.LogInformation("Total history entries migrated: {Count}", allHistory.Count);

    // Migrate settings (from first/primary user)
    var sqlSettings = db.AppSettings.AsNoTracking().FirstOrDefault(s => s.UserId == sourceUser.Id);
    var sqlRules = db.JobRules.AsNoTracking().Where(r => r.UserId == sourceUser.Id).ToList();

    var appSettings = new AppSettings();
    if (sqlSettings != null)
    {
        appSettings.JobSiteUrls = new JobSiteUrls
        {
            LinkedIn = sqlSettings.LinkedInUrl,
            S1Jobs = sqlSettings.S1JobsUrl,
            Indeed = sqlSettings.IndeedUrl,
            WTTJ = sqlSettings.WTTJUrl
        };
        appSettings.JobRules = new JobRulesSettings
        {
            EnableAutoRules = sqlSettings.EnableAutoRules,
            StopOnFirstMatch = sqlSettings.StopOnFirstMatch,
            Rules = sqlRules
        };
    }
    else
    {
        appSettings.JobRules.Rules = sqlRules;
    }

    // Reassign rule user IDs
    foreach (var rule in appSettings.JobRules.Rules)
    {
        rule.UserId = localUserId;
    }

    jsonStorage.SaveSettings(appSettings, localUserId);
    logger.LogInformation("Migrated settings ({RuleCount} rules)", appSettings.JobRules.Rules.Count);

    logger.LogInformation("Migration complete! Data written to {Dir}", dataDir);
    logger.LogInformation("Set \"LocalMode\": true in appsettings.json to use local storage.");
}

// Request model for interest update
record InterestUpdateRequest(InterestStatus Interest);

// Request model for availability check
record CheckAvailabilityRequest(string Source);

// Request model for marking job unavailable
record MarkUnavailableRequest(string? Reason);

// Request model for marking job unavailable by URL
record MarkUnavailableByUrlRequest(string Url, string? Reason);

// Request model for description update
record DescriptionUpdateRequest(string Url, string? Description, string? Company = null);

// Hangfire dashboard authorization filter — requires authenticated user
class HangfireAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User?.Identity?.IsAuthenticated == true;
    }
}
