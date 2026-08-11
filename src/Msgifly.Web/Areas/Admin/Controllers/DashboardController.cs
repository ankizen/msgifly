using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var model = new DashboardViewModel
        {
            ContactCount = await _db.Contacts.CountAsync(),
            CampaignCount = await _db.Campaigns.CountAsync(),
            TemplateCount = await _db.WhatsappTemplates.CountAsync(),
            MessageCount = await _db.ChatMessages.CountAsync(),
        };

        return View(model);
    }
}

public class DashboardViewModel
{
    public int ContactCount { get; set; }
    public int CampaignCount { get; set; }
    public int TemplateCount { get; set; }
    public int MessageCount { get; set; }
}
