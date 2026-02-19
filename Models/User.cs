namespace JobTracker.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;  // unique, used for login
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }  // TOTP secret
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpiry { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
