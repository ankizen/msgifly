using Msgifly.Web.Models.Enums;

namespace Msgifly.Web.Services.Email.Providers;

/// <summary>Resolves the right IEmailProviderHandler for a connection's Provider — mirrors
/// FluentSMTP's Providers/Factory.php.</summary>
public class EmailProviderHandlerFactory
{
    private readonly Dictionary<EmailSmtpProvider, IEmailProviderHandler> _handlers;

    public EmailProviderHandlerFactory(IEnumerable<IEmailProviderHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Provider);
    }

    public IEmailProviderHandler Resolve(EmailSmtpProvider provider) => _handlers[provider];
}
