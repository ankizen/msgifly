using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Msgifly.Web.Hubs;

/// <summary>
/// Realtime push for the chat inbox — the .NET-native replacement for the original's raw Pusher
/// REST-trigger calls (master doc §5.7/§12: "model this as calling Pusher/SignalR from a few
/// call sites," not a pub/sub event system). The hub itself has no client-invokable methods;
/// server code (the inbound webhook, the agent-send endpoint) broadcasts through
/// IHubContext&lt;ChatHub&gt; and connected inbox pages just listen.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
}
