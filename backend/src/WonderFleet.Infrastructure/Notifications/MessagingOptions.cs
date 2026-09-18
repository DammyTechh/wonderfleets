namespace WonderFleet.Infrastructure.Notifications;

public sealed class EmailOptions
{
    public const string Section = "Email";
    /// ResendApi (HTTPS) | ResendSmtp (smtp.resend.com) | Log (development: writes to the log only).
    public string Provider { get; set; } = "Log";
    public string From { get; set; } = "WonderFleet <alerts@wonderfleet.app>";
    public string? ReplyTo { get; set; }
    /// Resend API key (re_...). Used as the SMTP password for ResendSmtp.
    public string? ApiKey { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.resend.com";
    public string SmtpHost { get; set; } = "smtp.resend.com";
    public int SmtpPort { get; set; } = 465;
    public string SmtpUsername { get; set; } = "resend";
}

public sealed class SmsOptions
{
    public const string Section = "Sms";
    /// Termii | Twilio | Log
    public string Provider { get; set; } = "Log";
    public TermiiOptions Termii { get; set; } = new();
    public TwilioOptions Twilio { get; set; } = new();
}

public sealed class TermiiOptions
{
    /// Account-specific base URL from the Termii dashboard.
    public string BaseUrl { get; set; } = "https://v3.api.termii.com";
    public string? ApiKey { get; set; }
    /// Approved sender ID (max 11 chars).
    public string SenderId { get; set; } = "WonderFleet";
    /// "dnd" reaches DND-enabled Nigerian numbers for transactional traffic; "generic" does not.
    public string Channel { get; set; } = "dnd";
}

public sealed class TwilioOptions
{
    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    public string? FromNumber { get; set; }
    public string? MessagingServiceSid { get; set; }
}
