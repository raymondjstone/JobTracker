using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace JobTracker.Services;

public class EmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
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

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        try
        {
            var smtpHost = _configuration["Smtp:Host"];
            var smtpPortStr = _configuration["Smtp:Port"];
            var smtpUsername = _configuration["Smtp:Username"];
            var smtpPassword = _configuration["Smtp:Password"];
            var fromEmail = _configuration["Smtp:FromEmail"];
            var fromName = _configuration["Smtp:FromName"] ?? "Job Tracker";

            if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpPortStr))
            {
                _logger.LogWarning("SMTP not configured. Email not sent to {Email}", toEmail);
                return true;
            }

            var smtpPort = int.Parse(smtpPortStr);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromEmail ?? smtpUsername ?? "noreply@example.com"));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();

            // Port 465 uses implicit SSL, port 587 uses STARTTLS
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
