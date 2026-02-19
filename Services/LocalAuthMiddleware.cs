using System.Security.Claims;

namespace JobTracker.Services;

public class LocalAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthService _authService;
    private readonly ILogger<LocalAuthMiddleware> _logger;

    public LocalAuthMiddleware(RequestDelegate next, AuthService authService, ILogger<LocalAuthMiddleware> logger)
    {
        _next = next;
        _authService = authService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip if request already has an API key (let normal API key auth handle it)
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
        {
            await _next(context);
            return;
        }

        var user = GetLocalUser();
        if (user != null)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Name),
            };
            var identity = new ClaimsIdentity(claims, "LocalMode");
            context.User = new ClaimsPrincipal(identity);
        }

        await _next(context);
    }

    private Models.User? GetLocalUser()
    {
        var users = _authService.GetAllUsers();
        return users.FirstOrDefault(u => u.Email == "local@localhost") ?? users.FirstOrDefault();
    }
}
