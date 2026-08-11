using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Msgifly.Web.Controllers;

/// <summary>
/// Serves a standalone status page at the api.msgifly.com root — this app doesn't (yet) expose
/// a separate public REST API surface (see master doc §12 for that), so for now this is just a
/// human-facing "yes, the backend is up" landing page, matching the pattern used on the user's
/// other services (e.g. their BackupApi status page). Deliberately self-contained (no _Layout,
/// no Tailwind bundle dependency) so it stays fast/robust as a pure health signal.
/// </summary>
[Host("api.msgifly.com")]
public class StatusController : Controller
{
    [HttpGet("/")]
    public ContentResult Index()
    {
        const string html = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Msgifly API</title>
                <style>
                    * { box-sizing: border-box; }
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                        background: linear-gradient(160deg, #eef2ff 0%, #f8faff 60%, #ffffff 100%);
                    }
                    .card {
                        background: #ffffff;
                        border-radius: 20px;
                        box-shadow: 0 20px 60px rgba(30, 41, 59, 0.10);
                        padding: 48px 56px;
                        text-align: center;
                        max-width: 460px;
                    }
                    .pill {
                        display: inline-flex;
                        align-items: center;
                        gap: 8px;
                        font-size: 14px;
                        font-weight: 600;
                        color: #16a34a;
                        margin-bottom: 18px;
                    }
                    .dot {
                        width: 10px;
                        height: 10px;
                        border-radius: 999px;
                        background: #22c55e;
                        box-shadow: 0 0 0 4px rgba(34, 197, 94, 0.18);
                        animation: pulse 2s ease-in-out infinite;
                    }
                    @keyframes pulse {
                        0%, 100% { opacity: 1; }
                        50% { opacity: 0.55; }
                    }
                    h1 {
                        margin: 0 0 12px;
                        font-size: 28px;
                        font-weight: 800;
                        color: #0f172a;
                    }
                    p {
                        margin: 0;
                        font-size: 15px;
                        color: #64748b;
                        line-height: 1.5;
                    }
                </style>
            </head>
            <body>
                <div class="card">
                    <div class="pill"><span class="dot"></span> API is online</div>
                    <h1>Msgifly API is Running</h1>
                    <p>The backend service is online and accepting requests.</p>
                </div>
            </body>
            </html>
            """;

        return Content(html, "text/html");
    }
}
