using System.Net;
using System.Net.Sockets;

namespace Msgifly.Web.Services.Automations;

/// <summary>
/// SSRF guard for the SendWebhook automation step. The target URL is user-supplied and the
/// server makes the request on the user's behalf, so it must never be able to reach loopback,
/// link-local, or private-range addresses — otherwise a webhook step becomes a way to probe or
/// call the app's own internal network (the SQL Server container, the Coolify/Traefik proxy,
/// cloud metadata endpoints, etc.).
/// </summary>
public static class WebhookUrlGuard
{
    public static async Task<bool> IsDeliverableAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host);
        }
        catch (SocketException)
        {
            return false;
        }

        if (addresses.Length == 0)
        {
            return false;
        }

        return addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16 (link-local), 0.0.0.0/8
            if (bytes[0] == 10) return false;
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return false;
            if (bytes[0] == 192 && bytes[1] == 168) return false;
            if (bytes[0] == 169 && bytes[1] == 254) return false;
            if (bytes[0] == 0) return false;
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return false;
            }

            // fc00::/7 — unique local addresses.
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return false;
            }

            return true;
        }

        return false;
    }
}
