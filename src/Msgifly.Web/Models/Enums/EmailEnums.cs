namespace Msgifly.Web.Models.Enums;

/// <summary>Subscriber's opt-in state. Only Subscribed and Transactional are bulk-sendable —
/// Pending awaits confirmation, Unsubscribed/Bounced/Complained are permanently excluded from campaigns.</summary>
public enum EmailSubscriberStatus
{
    Subscribed = 0,
    Pending = 1,
    Unsubscribed = 2,
    Bounced = 3,
    Complained = 4,
    Transactional = 5,
}

public enum EmailCampaignStatus
{
    Draft = 0,
    Scheduled = 1,
    Sending = 2,
    Sent = 3,
    Paused = 4,
}

public enum EmailCampaignRecipientStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}

public enum EmailLogStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}

public enum EmailCustomFieldType
{
    Text = 0,
    Number = 1,
    Date = 2,
    Dropdown = 3,
}

/// <summary>Mirrors FluentSMTP's provider set (Providers/config.php) — real HTTP API
/// integrations, not just generic SMTP relay.</summary>
public enum EmailSmtpProvider
{
    Smtp = 0,
    Brevo = 1,
    SendGrid = 2,
    Mailgun = 3,
    AmazonSes = 4,
    Postmark = 5,
    SparkPost = 6,
    Netcore = 7,
    ElasticMail = 8,
    Smtp2Go = 9,
    Cloudflare = 10,
}
