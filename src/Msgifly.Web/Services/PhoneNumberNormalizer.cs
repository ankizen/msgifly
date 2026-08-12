namespace Msgifly.Web.Services;

/// <summary>
/// Every Contact.Phone write goes through this, and every dedup lookup compares against it —
/// otherwise the same real person ends up as two different Contact rows depending on which
/// source formatted their number differently (the WhatsApp webhook's `from` field is always raw
/// digits like "918208678144"; a Lead Ads form's phone_number field can come back as
/// "+91 82086 78144"; a human typing into the manual Add Contact form could enter almost
/// anything). Canonical form is digits-only, no "+", no spaces/dashes — matches what the webhook
/// already hands us natively, so that's the one source that never needed normalizing anyway.
/// </summary>
public static class PhoneNumberNormalizer
{
    public static string Normalize(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? string.Empty : new string(phone.Where(char.IsDigit).ToArray());
}
