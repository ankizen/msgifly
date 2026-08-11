using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Msgifly.Web.Extensions;

/// <summary>
/// Flash-toast helper, the equivalent of the original's Livewire `$this->notify()` macro —
/// consumed by Views/Shared/_Notification.cshtml via TempData["Notification"] after a redirect.
/// </summary>
public static class ControllerNotifyExtensions
{
    public static void Notify(this Controller controller, string message, string type = "success")
    {
        controller.TempData["Notification"] = JsonSerializer.Serialize(new { message, type });
    }
}
