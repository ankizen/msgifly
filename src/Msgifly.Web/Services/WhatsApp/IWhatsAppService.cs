namespace Msgifly.Web.Services.WhatsApp;

public interface IWhatsAppService
{
    Task<WhatsAppResult<List<PhoneNumberInfo>>> GetPhoneNumbersAsync();

    Task<WhatsAppResult<BusinessProfileInfo>> GetBusinessProfileAsync(string phoneNumberId);

    /// <summary>Pulls Meta-approved templates into the local WhatsappTemplate table (updateOrCreate + delete orphans). Returns the count synced.</summary>
    Task<WhatsAppResult<int>> SyncTemplatesAsync();

    /// <summary>Subscribes this app to the WABA's webhook events (messages, template status updates, etc.).</summary>
    Task<WhatsAppResult> SubscribeWebhookAsync();

    Task<WhatsAppResult> SendTestMessageAsync(string toPhoneNumber, string messageText);

    /// <summary>Sends an approved template message. Returns the WhatsApp message id (wamid) on success.</summary>
    Task<WhatsAppResult<string>> SendTemplateMessageAsync(string toPhoneNumber, TemplateSendRequest request);

    Task<WhatsAppResult<string>> DebugTokenAsync();
}
