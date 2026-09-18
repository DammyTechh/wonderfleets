using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Infrastructure.Notifications;

/// Resend over HTTPS (recommended on Render: no SMTP egress concerns, idempotent retries).
internal sealed class ResendApiEmailSender(HttpClient http, IOptions<EmailOptions> options) : IEmailSender
{
    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ApiKey)) return new SendResult(false, null, "Email:ApiKey is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.ApiBaseUrl.TrimEnd('/')}/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
        request.Content = JsonContent.Create(new Dictionary<string, object?>
        {
            ["from"] = o.From,
            ["to"] = new[] { message.To },
            ["subject"] = message.Subject,
            ["html"] = message.HtmlBody,
            ["text"] = message.TextBody,
            ["reply_to"] = o.ReplyTo,
        });

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new SendResult(false, null, $"Resend {(int)response.StatusCode}: {Trim(body)}");

        using var doc = JsonDocument.Parse(body);
        return new SendResult(true, doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null, null);
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}

/// Resend over SMTP (smtp.resend.com, username "resend", password = API key).
internal sealed class ResendSmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ApiKey)) return new SendResult(false, null, "Email:ApiKey is not configured.");

        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(o.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        if (!string.IsNullOrWhiteSpace(o.ReplyTo)) mime.ReplyTo.Add(MailboxAddress.Parse(o.ReplyTo));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 30_000 };
        var security = o.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(o.SmtpHost, o.SmtpPort, security, ct);
        await client.AuthenticateAsync(o.SmtpUsername, o.ApiKey, ct);
        var response = await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
        return new SendResult(true, mime.MessageId ?? response, null);
    }
}

internal sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogInformation("[email:log] to={To} subject={Subject}", Mask(message.To), message.Subject);
        return Task.FromResult(new SendResult(true, "log-" + Guid.NewGuid().ToString("N")[..12], null));
    }

    internal static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? "***" : email[0] + "***" + email[at..];
    }
}

/// Termii (Nigeria). The "dnd" channel is required to reach numbers on the NCC Do-Not-Disturb list.
internal sealed class TermiiSmsSender(HttpClient http, IOptions<SmsOptions> options) : ISmsSender
{
    public async Task<SendResult> SendAsync(string phoneE164, string message, CancellationToken ct)
    {
        var o = options.Value.Termii;
        if (string.IsNullOrWhiteSpace(o.ApiKey)) return new SendResult(false, null, "Sms:Termii:ApiKey is not configured.");

        using var response = await http.PostAsJsonAsync($"{o.BaseUrl.TrimEnd('/')}/api/sms/send", new Dictionary<string, object>
        {
            ["api_key"] = o.ApiKey,
            ["to"] = phoneE164.TrimStart('+'),
            ["from"] = o.SenderId,
            ["sms"] = message,
            ["type"] = "plain",
            ["channel"] = o.Channel,
        }, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new SendResult(false, null, $"Termii {(int)response.StatusCode}: {(body.Length > 300 ? body[..300] : body)}");

        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.TryGetProperty("message_id", out var m) ? m.ToString() : null;
        return new SendResult(true, id, null);
    }
}

internal sealed class TwilioSmsSender(HttpClient http, IOptions<SmsOptions> options) : ISmsSender
{
    public async Task<SendResult> SendAsync(string phoneE164, string message, CancellationToken ct)
    {
        var o = options.Value.Twilio;
        if (string.IsNullOrWhiteSpace(o.AccountSid) || string.IsNullOrWhiteSpace(o.AuthToken))
            return new SendResult(false, null, "Sms:Twilio credentials are not configured.");

        var form = new Dictionary<string, string> { ["To"] = phoneE164, ["Body"] = message };
        if (!string.IsNullOrWhiteSpace(o.MessagingServiceSid)) form["MessagingServiceSid"] = o.MessagingServiceSid;
        else if (!string.IsNullOrWhiteSpace(o.FromNumber)) form["From"] = o.FromNumber;
        else return new SendResult(false, null, "Sms:Twilio needs FromNumber or MessagingServiceSid.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(o.AccountSid)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{o.AccountSid}:{o.AuthToken}")));

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new SendResult(false, null, $"Twilio {(int)response.StatusCode}: {(body.Length > 300 ? body[..300] : body)}");
        using var doc = JsonDocument.Parse(body);
        return new SendResult(true, doc.RootElement.TryGetProperty("sid", out var sid) ? sid.GetString() : null, null);
    }
}

internal sealed class LogSmsSender(ILogger<LogSmsSender> logger) : ISmsSender
{
    public Task<SendResult> SendAsync(string phoneE164, string message, CancellationToken ct)
    {
        var masked = phoneE164.Length > 6 ? phoneE164[..4] + new string('*', phoneE164.Length - 7) + phoneE164[^3..] : "***";
        logger.LogInformation("[sms:log] to={To} chars={Length}", masked, message.Length);
        return Task.FromResult(new SendResult(true, "log-" + Guid.NewGuid().ToString("N")[..12], null));
    }
}
