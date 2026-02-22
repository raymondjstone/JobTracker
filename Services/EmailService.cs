using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using JobTracker.Models;

namespace JobTracker.Services;

public class EmailService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IServiceProvider serviceProvider, ILogger<EmailService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetToken, string baseUrl)
    {
        var resetUrl = $"{baseUrl}/reset-password?token={resetToken}";

        var subject = "Job Tracker - Password Reset Request";
        var body = $@"
<html>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333;"">
    <div style=""max-width: 600px; margin: 0 auto; padding: 20px;"">
        <h2 style=""color: #0066cc;"">Password Reset Request</h2>
        <p>You requested a password reset for your Job Tracker account.</p>
        <p>Click the button below to reset your password:</p>
        <p style=""margin: 30px 0;"">
            <a href=""{resetUrl}""
               style=""background-color: #0066cc; color: white; padding: 12px 24px;
                      text-decoration: none; border-radius: 4px; display: inline-block;"">
                Reset Password
            </a>
        </p>
        <p>Or copy and paste this link into your browser:</p>
        <p style=""word-break: break-all; color: #666;"">{resetUrl}</p>
        <p>This link will expire in 24 hours.</p>
        <p>If you didn't request this password reset, you can safely ignore this email.</p>
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"" />
        <p style=""color: #999; font-size: 12px;"">
            This is an automated message from Job Tracker. Please do not reply to this email.
        </p>
    </div>
</body>
</html>";

        return await SendEmailAsync(toEmail, subject, body);
    }

    /// <summary>
    /// Send email using SMTP settings from the recipient user's account
    /// </summary>
    public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        // Create a scope to resolve scoped services
        using var scope = _serviceProvider.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();

        // Find the user by their email address
        var user = authService.GetUserByEmail(toEmail);
        if (user == null)
        {
            _logger.LogWarning("User not found for email {Email}. Cannot send email.", toEmail);
            return false;
        }

        // Get the user's SMTP settings
        var settings = settingsService.GetSettings(user.Id);

        if (string.IsNullOrEmpty(settings.SmtpHost))
        {
            _logger.LogWarning("SMTP not configured for user {Email}. Email not sent. User should configure SMTP in Settings page.", toEmail);
            return false;
        }

        return await SendEmailAsync(toEmail, subject, htmlBody,
            settings.SmtpHost, settings.SmtpPort, settings.SmtpUsername, settings.SmtpPassword,
            settings.SmtpFromEmail, settings.SmtpFromName);
    }

    /// <summary>
    /// Send email using explicit SMTP settings (for per-user settings).
    /// </summary>
    public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody,
        string smtpHost, int smtpPort, string smtpUsername, string smtpPassword,
        string fromEmail, string fromName)
    {
        if (string.IsNullOrEmpty(smtpHost))
        {
            _logger.LogWarning("SMTP host not configured. Email not sent to {Email}", toEmail);
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                string.IsNullOrEmpty(fromName) ? "Job Tracker" : fromName,
                string.IsNullOrEmpty(fromEmail) ? smtpUsername : fromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();

            var secureSocketOptions = smtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await client.ConnectAsync(smtpHost, smtpPort, secureSocketOptions);

            if (!string.IsNullOrEmpty(smtpUsername) && !string.IsNullOrEmpty(smtpPassword))
            {
                await client.AuthenticateAsync(smtpUsername, smtpPassword);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}: {Subject}", toEmail, subject);
            return false;
        }
    }
}
