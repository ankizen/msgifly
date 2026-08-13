namespace Msgifly.Web.Services.WhatsApp;

public interface IWhatsAppService
{
    Task<WhatsAppResult<List<PhoneNumberInfo>>> GetPhoneNumbersAsync();

    /// <summary>
    /// Cloud API numbers need an explicit one-time registration (a 2-step-verification PIN) before
    /// they can send/receive — separate from the number's ownership/display-name verification,
    /// which is what GetPhoneNumbersAsync's data already reflects. Skipping this is exactly what
    /// produces Meta's error #133010 "Account not registered" on an otherwise fully-connected number.
    /// </summary>
    Task<WhatsAppResult> RegisterPhoneNumberAsync(string phoneNumberId, string pin);

    Task<WhatsAppResult<BusinessProfileInfo>> GetBusinessProfileAsync(string phoneNumberId);

    /// <summary>Pulls Meta-approved templates into the local WhatsappTemplate table (updateOrCreate + delete orphans). Returns the count synced.</summary>
    Task<WhatsAppResult<int>> SyncTemplatesAsync();

    /// <summary>Subscribes this app to the WABA's webhook events (messages, template status updates, etc.).</summary>
    Task<WhatsAppResult> SubscribeWebhookAsync();

    Task<WhatsAppResult> SendTestMessageAsync(string toPhoneNumber, string messageText);

    /// <summary>Returns the WhatsApp message id (wamid) on success — needed to correlate later delivery-status webhooks.</summary>
    Task<WhatsAppResult<string>> SendPlainTextMessageAsync(string toPhoneNumber, string messageText);

    /// <summary>Sends an approved template message. Returns the WhatsApp message id (wamid) on success.</summary>
    Task<WhatsAppResult<string>> SendTemplateMessageAsync(string toPhoneNumber, TemplateSendRequest request);

    Task<WhatsAppResult<string>> DebugTokenAsync();

    // ---- Template lifecycle (create/edit/delete — sync already covers read) ----

    /// <summary>Validates and submits a new template to Meta for approval, then upserts the local row (status PENDING). Throws ArgumentException on validation failure.</summary>
    Task<WhatsAppResult<Models.Entities.WhatsappTemplate>> CreateTemplateAsync(TemplateCreateRequest request);

    /// <summary>Re-submits an existing APPROVED/REJECTED/PAUSED template with new content — Meta replaces components wholesale and resets status to PENDING. Throws ArgumentException on validation failure.</summary>
    Task<WhatsAppResult<Models.Entities.WhatsappTemplate>> EditTemplateAsync(int localTemplateId, TemplateCreateRequest request);

    /// <summary>Deletes on Meta (when previously submitted) and removes the local row.</summary>
    Task<WhatsAppResult> DeleteTemplateAsync(int localTemplateId);

    // ---- Media API ----

    /// <summary>Uploads a file to Meta's servers against the given phone number, returning a media_id usable in outbound messages or as a whatsapp_business_profile photo handle (Graph API's /media endpoint requires this per-file upload; ids expire after ~30 days).</summary>
    Task<WhatsAppResult<string>> UploadMediaAsync(string phoneNumberId, Stream fileStream, string fileName, string mimeType);

    /// <summary>Resolves a media_id (from an inbound message, or one of ours) to a short-lived signed CDN URL + metadata.</summary>
    Task<WhatsAppResult<MediaInfo>> GetMediaInfoAsync(string mediaId);

    /// <summary>Downloads the actual bytes from a signed CDN URL returned by GetMediaInfoAsync — these URLs require the same Bearer token as the Graph API itself.</summary>
    Task<WhatsAppResult<byte[]>> DownloadMediaBytesAsync(string mediaUrl);

    Task<WhatsAppResult> DeleteMediaAsync(string mediaId);

    // ---- Outbound message types ----

    /// <summary>Sends an image/video/audio/document/sticker by public link or previously-uploaded media_id.</summary>
    Task<WhatsAppResult<string>> SendMediaMessageAsync(string toPhoneNumber, MediaMessageRequest request);

    Task<WhatsAppResult<string>> SendLocationMessageAsync(string toPhoneNumber, LocationMessageRequest request);

    Task<WhatsAppResult<string>> SendContactMessageAsync(string toPhoneNumber, ContactCardRequest contact);

    /// <summary>Reacts to a specific inbound/outbound message. Pass an empty emoji string to remove a reaction.</summary>
    Task<WhatsAppResult> SendReactionAsync(string toPhoneNumber, string messageId, string emoji);

    /// <summary>Marks an inbound message as read (blue ticks) and, per Meta's behavior, also shows the typing indicator briefly beforehand.</summary>
    Task<WhatsAppResult> MarkMessageAsReadAsync(string messageId);

    /// <summary>Up to 3 quick-reply buttons under a body message.</summary>
    Task<WhatsAppResult<string>> SendInteractiveButtonsMessageAsync(string toPhoneNumber, string bodyText, List<InteractiveButton> buttons, string? headerText = null, string? footerText = null);

    /// <summary>A single "Menu"-style button that opens a scrollable list of selectable rows, grouped into sections.</summary>
    Task<WhatsAppResult<string>> SendInteractiveListMessageAsync(string toPhoneNumber, string bodyText, string buttonText, List<InteractiveListSection> sections, string? headerText = null, string? footerText = null);

    /// <summary>A single button that opens an external URL.</summary>
    Task<WhatsAppResult<string>> SendInteractiveCtaUrlMessageAsync(string toPhoneNumber, string bodyText, string buttonText, string url, string? headerText = null, string? footerText = null);

    // ---- Business profile ----

    Task<WhatsAppResult> UpdateBusinessProfileAsync(string phoneNumberId, BusinessProfileUpdateRequest request);

    /// <summary>Uploads a new profile photo — takes the {h} handle from UploadProfilePictureHandleAsync (NOT a media_id from UploadMediaAsync; those are different Meta upload mechanisms).</summary>
    Task<WhatsAppResult> UpdateBusinessProfilePictureAsync(string phoneNumberId, string profilePictureHandle);

    /// <summary>Runs Meta's two-step Resumable Upload API against the current Meta App to get a photo handle for UpdateBusinessProfilePictureAsync.</summary>
    Task<WhatsAppResult<string>> UploadProfilePictureHandleAsync(Stream fileStream, string fileName, long fileLength, string mimeType);

    // ---- Conversational automation (ice breakers + commands menu) ----

    Task<WhatsAppResult<ConversationalAutomationInfo>> GetConversationalAutomationAsync(string phoneNumberId);

    /// <summary>Overwrites both lists wholesale — Meta's endpoint takes commands+prompts together in one call, not an append.</summary>
    Task<WhatsAppResult> UpdateConversationalAutomationAsync(string phoneNumberId, List<string> prompts, List<CommandInfo> commands);
}
