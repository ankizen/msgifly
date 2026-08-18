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

    // Static snapshot (fetched 2026-08-18), not a live feed — good enough for an approximate
    // cross-currency read, not for anything that needs to be exact to the day. Units per 1 USD.
    private static readonly Dictionary<string, decimal> UsdRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1m,
        ["INR"] = 95.75m,
        ["AED"] = 3.6725m,
    };

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

        var dataPoints = result.Data!.DataPoints;
        model.Currency = result.Data!.Currency;
        model.TotalCost = dataPoints.Sum(d => d.Cost);
        model.TotalVolume = dataPoints.Sum(d => d.Volume);
        model.ConvertedTotals = ConvertToDisplayCurrencies(model.TotalCost, model.Currency);

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

    /// <summary>Converts one amount into every currency in UsdRates (including its own source
    /// currency, so the page can show USD/INR/AED side by side regardless of which one Meta
    /// actually bills in). Returns empty if the source currency isn't in the static table.</summary>
    private static List<BillingCurrencyRow> ConvertToDisplayCurrencies(decimal amount, string? sourceCurrency)
    {
        if (string.IsNullOrWhiteSpace(sourceCurrency) || !UsdRates.TryGetValue(sourceCurrency, out var sourceRate))
        {
            return [];
        }

        var usdAmount = amount / sourceRate;
        return [.. UsdRates.Select(kv => new BillingCurrencyRow(kv.Key.ToUpperInvariant(), usdAmount * kv.Value))];
    }
}
