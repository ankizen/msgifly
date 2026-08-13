using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class BillingController : Controller
{
    private static readonly DateTime MetaLookbackFloor = DateTime.UtcNow.AddYears(-1);

    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public BillingController(ApplicationDbContext db, IWhatsAppService whatsAppService, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "billing.view")]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;
        var toDate = (to ?? now).Date.AddDays(1).AddTicks(-1);
        var fromDate = (from ?? new DateTime(now.Year, now.Month, 1)).Date;
        if (fromDate < MetaLookbackFloor)
        {
            fromDate = MetaLookbackFloor.Date;
        }

        var model = new BillingViewModel { FromDate = fromDate, ToDate = toDate.Date };

        var isConnected = await _db.Workspaces.AsNoTracking()
            .Where(w => w.Id == _workspaceAccessor.WorkspaceId)
            .Select(w => w.IsAccountConnected)
            .FirstOrDefaultAsync();
        model.IsConnected = isConnected;

        if (!isConnected)
        {
            model.ErrorMessage = "Connect a WhatsApp Business Account first.";
            return View(model);
        }

        var result = await _whatsAppService.GetPricingAnalyticsAsync(fromDate, toDate, "DAILY", ["PRICING_CATEGORY"]);
        if (!result.Success)
        {
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }

        var dataPoints = result.Data!;
        model.TotalCost = dataPoints.Sum(d => d.Cost);
        model.TotalVolume = dataPoints.Sum(d => d.Volume);

        model.ByCategory = [.. dataPoints
            .GroupBy(d => d.PricingCategory ?? "(uncategorized)")
            .Select(g => new BillingCategoryRow(g.Key, g.Sum(d => d.Volume), g.Sum(d => d.Cost)))
            .OrderByDescending(r => r.Cost)];

        model.Trend = [.. dataPoints
            .GroupBy(d => d.PeriodStart.Date)
            .Select(g => new BillingTrendRow(g.Key, g.Sum(d => d.Cost)))
            .OrderBy(r => r.Date)];

        return View(model);
    }
}
