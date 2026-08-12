namespace Msgifly.Web.Services.ApiKeys;

/// <summary>
/// The public API is authorized by scopes only — a key's capabilities are exactly what was
/// granted at creation, independent of who created it (key creation itself is gated to admins
/// in the dashboard, but the API request path only ever checks scopes). Adding a capability is
/// one entry here plus the endpoint that checks it — no migration needed, scopes are stored as
/// free text.
/// </summary>
public static class ApiScopes
{
    public const string MessagesSend = "messages:send";
    public const string MessagesRead = "messages:read";
    public const string ContactsRead = "contacts:read";
    public const string ContactsWrite = "contacts:write";
    public const string ConversationsRead = "conversations:read";

    public static readonly string[] All =
    [
        MessagesSend, MessagesRead, ContactsRead, ContactsWrite, ConversationsRead,
    ];

    public static readonly Dictionary<string, string> Descriptions = new()
    {
        [MessagesSend] = "Send WhatsApp messages",
        [MessagesRead] = "Read messages and their delivery status",
        [ContactsRead] = "List and read contacts",
        [ContactsWrite] = "Create and update contacts",
        [ConversationsRead] = "List and read conversations",
    };
}
