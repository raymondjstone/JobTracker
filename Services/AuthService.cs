using JobTracker.Models;
using Microsoft.AspNetCore.Identity;
using OtpNet;
using System.Security.Claims;
using System.Security.Cryptography;

namespace JobTracker.Services;

public class AuthService
{
    private readonly IStorageBackend _storage;
    private readonly ILogger<AuthService> _logger;
    private readonly PasswordHasher<User> _passwordHasher;

    // Rate limiting: track failed login attempts per email
    private readonly Dictionary<string, (int Count, DateTime FirstFailure)> _failedAttempts = new();
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public AuthService(IStorageBackend storage, ILogger<AuthService> logger)
    {
        _storage = storage;
        _logger = logger;
        _passwordHasher = new PasswordHasher<User>();
    }

    public User? GetUserById(Guid id) => _storage.GetUserById(id);
    public User? GetUserByEmail(string email) => _storage.GetUserByEmail(email);
    public List<User> GetAllUsers() => _storage.GetAllUsers();

    /// <summary>
    /// Update a user's email address
    /// </summary>
    public bool UpdateUserEmail(Guid userId, string newEmail)
    {
        var user = _storage.GetUserById(userId);
        if (user == null)
        {
            _logger.LogWarning("Cannot update email - user not found: {UserId}", userId);
            return false;
        }

        var normalizedEmail = newEmail.ToLowerInvariant();

        // Check if email is already taken by another user
        var existingUser = _storage.GetUserByEmail(normalizedEmail);
        if (existingUser != null && existingUser.Id != userId)
        {
            _logger.LogWarning("Cannot update email - already taken: {Email}", newEmail);
            return false;
        }

        user.Email = normalizedEmail;
        _storage.SaveUser(user);
        _logger.LogInformation("Updated user email: {UserId} -> {Email}", userId, normalizedEmail);
        return true;
    }

    /// <summary>
    /// Creates a new user with the given credentials
    /// </summary>
    public User CreateUser(string email, string name, string password)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            Name = name,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        _storage.AddUser(user);
        _logger.LogInformation("Created user: {Email}", email);
        return user;
    }

    /// <summary>
    /// Validates user credentials and returns the user if valid.
    /// Includes rate limiting: locks out after 5 failed attempts for 5 minutes.
    /// </summary>
    public User? ValidateCredentials(string email, string password)
    {
        var normalizedEmail = email.ToLowerInvariant();

        // Check rate limiting
        if (IsLockedOut(normalizedEmail))
        {
            _logger.LogWarning("Login attempt for locked-out user: {Email}", email);
            return null;
        }

        var user = _storage.GetUserByEmail(email);
        if (user == null)
        {
            RecordFailedAttempt(normalizedEmail);
            _logger.LogWarning("Login attempt for non-existent user: {Email}", email);
            return null;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            RecordFailedAttempt(normalizedEmail);
            _logger.LogWarning("Invalid password for user: {Email}", email);
            return null;
        }

        // Successful login — clear failed attempts
        ClearFailedAttempts(normalizedEmail);

        // If password needs rehash (upgraded algorithm), update it
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            _storage.SaveUser(user);
        }

        return user;
    }

    /// <summary>
    /// Returns true if the email is currently locked out due to too many failed attempts
    /// </summary>
    public bool IsLockedOut(string email)
    {
        var key = email.ToLowerInvariant();
        lock (_failedAttempts)
        {
            if (!_failedAttempts.TryGetValue(key, out var entry))
                return false;

            // If lockout window has expired, clear and allow
            if (DateTime.UtcNow - entry.FirstFailure > LockoutDuration)
            {
                _failedAttempts.Remove(key);
                return false;
            }

            return entry.Count >= MaxFailedAttempts;
        }
    }

    private void RecordFailedAttempt(string email)
    {
        lock (_failedAttempts)
        {
            if (_failedAttempts.TryGetValue(email, out var entry))
            {
                // If window expired, start fresh
                if (DateTime.UtcNow - entry.FirstFailure > LockoutDuration)
                    _failedAttempts[email] = (1, DateTime.UtcNow);
                else
                    _failedAttempts[email] = (entry.Count + 1, entry.FirstFailure);
            }
            else
            {
                _failedAttempts[email] = (1, DateTime.UtcNow);
            }
        }
    }

    private void ClearFailedAttempts(string email)
    {
        lock (_failedAttempts)
        {
            _failedAttempts.Remove(email);
        }
    }

    /// <summary>
    /// Validates an API key and returns the user if valid
    /// </summary>
    public User? ValidateApiKey(string apiKey)
    {
        var users = _storage.GetAllUsers();
        return users.FirstOrDefault(u => string.Equals(u.ApiKey, apiKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// Regenerates the API key for a user
    /// </summary>
    public string RegenerateApiKey(Guid userId)
    {
        var user = _storage.GetUserById(userId);
        if (user == null) throw new InvalidOperationException("User not found");

        user.ApiKey = User.GenerateNewApiKey();
        _storage.SaveUser(user);
        _logger.LogInformation("API key regenerated for user: {Email}", user.Email);
        return user.ApiKey;
    }

    /// <summary>
    /// Updates the user's last login timestamp
    /// </summary>
    public void RecordLogin(User user)
    {
        user.LastLoginAt = DateTime.UtcNow;
        _storage.SaveUser(user);
        _logger.LogInformation("User logged in: {Email}", user.Email);
    }

    /// <summary>
    /// Changes the user's password
    /// </summary>
    public bool ChangePassword(Guid userId, string currentPassword, string newPassword)
    {
        var user = _storage.GetUserById(userId);
        if (user == null) return false;

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword);
        if (result == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Password change failed - invalid current password for user: {Email}", user.Email);
            return false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        _storage.SaveUser(user);
        _logger.LogInformation("Password changed for user: {Email}", user.Email);
        return true;
    }

    public bool UpdateUserName(Guid userId, string name)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            _logger.LogWarning("User name update failed - blank name for user ID: {UserId}", userId);
            return false;
        }

        var user = _storage.GetUserById(userId);
        if (user == null) return false;

        user.Name = trimmedName;
        _storage.SaveUser(user);
        _logger.LogInformation("User name updated for user: {Email}", user.Email);
        return true;
    }

    /// <summary>
    /// Generates a password reset token for the user
    /// </summary>
    public string? GeneratePasswordResetToken(string email)
    {
        var user = _storage.GetUserByEmail(email);
        if (user == null) return null;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        user.PasswordResetToken = token;
        user.PasswordResetExpiry = DateTime.UtcNow.AddHours(24);
        _storage.SaveUser(user);

        _logger.LogInformation("Password reset token generated for user: {Email}", email);
        return token;
    }

    /// <summary>
    /// Validates a password reset token and returns the user if valid
    /// </summary>
    public User? ValidateResetToken(string token)
    {
        var user = _storage.GetUserByResetToken(token);
        if (user == null) return null;

        if (user.PasswordResetExpiry == null || user.PasswordResetExpiry < DateTime.UtcNow)
        {
            _logger.LogWarning("Expired password reset token used for user: {Email}", user.Email);
            return null;
        }

        return user;
    }

    /// <summary>
    /// Resets the user's password using a valid reset token
    /// </summary>
    public bool ResetPassword(string token, string newPassword)
    {
        var user = ValidateResetToken(token);
        if (user == null) return false;

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpiry = null;
        _storage.SaveUser(user);

        _logger.LogInformation("Password reset completed for user: {Email}", user.Email);
        return true;
    }

    // Two-Factor Authentication (TOTP)

    /// <summary>
    /// Generates a new 2FA secret for the user
    /// </summary>
    public string GenerateTwoFactorSecret(Guid userId)
    {
        var user = _storage.GetUserById(userId);
        if (user == null) throw new InvalidOperationException("User not found");

        // Generate a random secret (20 bytes = 160 bits, standard for TOTP)
        var secretBytes = RandomNumberGenerator.GetBytes(20);
        var secret = Base32Encoding.ToString(secretBytes);

        user.TwoFactorSecret = secret;
        _storage.SaveUser(user);

        return secret;
    }

    /// <summary>
    /// Gets the 2FA setup URI for authenticator apps (otpauth:// format)
    /// </summary>
    public string GetTwoFactorSetupUri(User user, string issuer = "JobTracker")
    {
        if (string.IsNullOrEmpty(user.TwoFactorSecret))
            throw new InvalidOperationException("2FA secret not generated");

        // Format: otpauth://totp/Issuer:account?secret=SECRET&issuer=Issuer
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedEmail = Uri.EscapeDataString(user.Email);
        return $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={user.TwoFactorSecret}&issuer={encodedIssuer}";
    }

    /// <summary>
    /// Validates a TOTP code for the user, with replay protection
    /// </summary>
    public bool ValidateTwoFactorCode(Guid userId, string code)
    {
        var user = _storage.GetUserById(userId);
        if (user == null || string.IsNullOrEmpty(user.TwoFactorSecret))
            return false;

        var secretBytes = Base32Encoding.ToBytes(user.TwoFactorSecret);
        var totp = new Totp(secretBytes);

        // Verify with a window of +/- 1 time step (30 seconds) to account for clock drift
        if (!totp.VerifyTotp(code, out long timeStepMatched, new VerificationWindow(previous: 1, future: 1)))
            return false;

        // Prevent TOTP replay: reject if this timestep was already used
        if (user.LastUsedTotpTimestep.HasValue && timeStepMatched <= user.LastUsedTotpTimestep.Value)
        {
            _logger.LogWarning("TOTP replay attempt for user {UserId}: timestep {Timestep} already used", userId, timeStepMatched);
            return false;
        }

        // Record the used timestep
        user.LastUsedTotpTimestep = timeStepMatched;
        _storage.SaveUser(user);

        return true;
    }

    /// <summary>
    /// Enables 2FA for the user after validating their first code
    /// </summary>
    public bool EnableTwoFactor(Guid userId, string code)
    {
        if (!ValidateTwoFactorCode(userId, code))
        {
            _logger.LogWarning("2FA enable failed - invalid code for user ID: {UserId}", userId);
            return false;
        }

        var user = _storage.GetUserById(userId);
        if (user == null) return false;

        user.TwoFactorEnabled = true;
        _storage.SaveUser(user);
        _logger.LogInformation("2FA enabled for user: {Email}", user.Email);
        return true;
    }

    /// <summary>
    /// Disables 2FA for the user
    /// </summary>
    public void DisableTwoFactor(Guid userId)
    {
        var user = _storage.GetUserById(userId);
        if (user == null) return;

        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        _storage.SaveUser(user);
        _logger.LogInformation("2FA disabled for user: {Email}", user.Email);
    }

    /// <summary>
    /// Creates claims for the authenticated user
    /// </summary>
    public ClaimsPrincipal CreateClaimsPrincipal(User user, bool twoFactorVerified = true)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("TwoFactorVerified", twoFactorVerified.ToString())
        };

        var identity = new ClaimsIdentity(claims, "ApplicationCookie");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Gets the user ID from the claims principal
    /// </summary>
    public static Guid? GetUserId(ClaimsPrincipal? principal)
    {
        if (principal == null) return null;

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return null;

        return Guid.TryParse(userIdClaim.Value, out var userId) ? userId : null;
    }

    /// <summary>
    /// Checks if the user has completed 2FA verification
    /// </summary>
    public static bool IsTwoFactorVerified(ClaimsPrincipal? principal)
    {
        if (principal == null) return false;

        var claim = principal.FindFirst("TwoFactorVerified");
        return claim != null && bool.TryParse(claim.Value, out var verified) && verified;
    }
}
