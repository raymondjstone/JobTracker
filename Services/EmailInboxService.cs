using JobTracker.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace JobTracker.Services;

public class IncomingEmail
{
    public string MessageId { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? TextBody { get; set; }
    public string? HtmlBody { get; set; }
    public DateTime Date { get; set; }
}

public class EmailInboxService
{
    private readonly ILogger<EmailInboxService> _logger;

    public EmailInboxService(ILogger<EmailInboxService> logger)
    {
        _logger = logger;
    }

    public async Task<List<IncomingEmail>> FetchNewEmailsAsync(AppSettings settings, HashSet<string> processedMessageIds)
    {
        var emails = new List<IncomingEmail>();
        var (host, port, useSsl, username, password, folder) = ResolveImapSettings(settings);

        if (string.IsNullOrWhiteSpace(host))
            return emails;

        using var client = new ImapClient();
        try
        {
            var secureSocketOptions = port == 993
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await client.ConnectAsync(host, port, secureSocketOptions);
            await client.AuthenticateAsync(username, password);

            var mailFolder = string.Equals(folder, "INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : await client.GetFolderAsync(folder);

            await mailFolder.OpenAsync(FolderAccess.ReadOnly);

            // Search for messages from the last 7 days
            var since = DateTime.Now.AddDays(-7);
            var query = SearchQuery.DeliveredAfter(since);
            var uids = await mailFolder.SearchAsync(query);

            _logger.LogInformation("[EmailInbox] Found {Count} messages in last 7 days in {Folder}", uids.Count, folder);

            var count = 0;
            foreach (var uid in uids.OrderByDescending(u => u.Id))
            {
                if (count >= 100) break;

                var message = await mailFolder.GetMessageAsync(uid);
                var messageId = message.MessageId ?? $"{uid.Id}@{host}";

                if (processedMessageIds.Contains(messageId))
                    continue;

                var from = message.From.Mailboxes.FirstOrDefault();
                emails.Add(new IncomingEmail
                {
                    MessageId = messageId,
                    From = from?.Name ?? from?.Address ?? "Unknown",
                    FromAddress = from?.Address ?? "",
                    Subject = message.Subject ?? "",
                    TextBody = message.TextBody,
                    HtmlBody = message.HtmlBody,
                    Date = message.Date.LocalDateTime
                });
                count++;
            }

            await client.DisconnectAsync(true);
            _logger.LogInformation("[EmailInbox] Fetched {Count} new emails", emails.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmailInbox] Error fetching emails from {Host}", host);
            try { await client.DisconnectAsync(false); } catch { }
            throw;
        }

        return emails;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(AppSettings settings)
    {
        var (host, port, useSsl, username, password, folder) = ResolveImapSettings(settings);

        if (string.IsNullOrWhiteSpace(host))
            return (false, "IMAP host is not configured.");

        using var client = new ImapClient();
        try
        {
            var secureSocketOptions = port == 993
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await client.ConnectAsync(host, port, secureSocketOptions);
            await client.AuthenticateAsync(username, password);

            var mailFolder = string.Equals(folder, "INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : await client.GetFolderAsync(folder);

            await mailFolder.OpenAsync(FolderAccess.ReadOnly);
            var messageCount = mailFolder.Count;
            await client.DisconnectAsync(true);

            return (true, $"Connected successfully. {messageCount} messages in {folder}.");
        }
        catch (Exception ex)
        {
            try { await client.DisconnectAsync(false); } catch { }
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    private static (string Host, int Port, bool UseSsl, string Username, string Password, string Folder)
        ResolveImapSettings(AppSettings settings)
    {
        var username = string.IsNullOrWhiteSpace(settings.ImapUsername)
            ? settings.SmtpUsername
            : settings.ImapUsername;
        var password = string.IsNullOrWhiteSpace(settings.ImapPassword)
            ? settings.SmtpPassword
            : settings.ImapPassword;

        return (settings.ImapHost, settings.ImapPort, settings.ImapUseSsl, username, password, settings.ImapFolder);
    }
}
