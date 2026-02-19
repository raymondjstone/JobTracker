using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace JobTracker.Services;

/// <summary>
/// Service to track and provide the current user's ID.
/// This is used by singleton services that need user context.
/// Uses HttpContext during prerendering/API calls and falls back to
/// AuthenticationStateProvider for interactive SignalR mode.
/// </summary>
public class CurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IConfiguration _configuration;
    private readonly AuthService _authService;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, AuthenticationStateProvider authStateProvider, IConfiguration configuration, AuthService authService)
    {
        _httpContextAccessor = httpContextAccessor;
        _authStateProvider = authStateProvider;
        _configuration = configuration;
        _authService = authService;
    }

    /// <summary>
    /// Gets the current user's ID from HttpContext or AuthenticationStateProvider.
    /// </summary>
    public Guid GetCurrentUserId()
    {
        // In LocalMode, always return the local user
        if (_configuration.GetValue<bool>("LocalMode"))
        {
            var localUser = _authService.GetAllUsers().FirstOrDefault();
            if (localUser != null) return localUser.Id;
        }

        // Try HttpContext first (works during prerendering and API calls)
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var id = ExtractUserId(user);
            if (id != Guid.Empty) return id;
        }

        // Fall back to AuthenticationStateProvider (works in interactive SignalR mode)
        try
        {
            var authState = _authStateProvider.GetAuthenticationStateAsync().GetAwaiter().GetResult();
            if (authState.User?.Identity?.IsAuthenticated == true)
            {
                var id = ExtractUserId(authState.User);
                if (id != Guid.Empty) return id;
            }
        }
        catch
        {
            // Ignore - may not be available in all contexts
        }

        return Guid.Empty;
    }

    /// <summary>
    /// Returns true if there is an authenticated user.
    /// </summary>
    public bool IsAuthenticated => GetCurrentUserId() != Guid.Empty;

    private static Guid ExtractUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        return Guid.Empty;
    }
}
