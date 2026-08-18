namespace Msgifly.Web.Models.ViewModels;

/// <summary>Backs the Billing/Spend page — per-message cost from Meta's pricing_analytics, since Msgifly has no other visibility into WhatsApp spend.</summary>
public class BillingViewModel
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    public decimal TotalCost { get; set; }
    public int TotalVolume { get; set; }

    /// <summary>ISO 4217 code TotalCost/ByCategory/Trend are denominated in — the WABA's own
    /// billing currency, straight from Meta (not user-configurable).</summary>
    public string? Currency { get; set; }

    /// <summary>TotalCost converted into a few common display currencies, for a workspace whose
    /// audience/spend context spans more than one — approximate, see BillingController's static
    /// rate table, not a live feed.</summary>
    public List<BillingCurrencyRow> ConvertedTotals { get; set; } = [];

    public List<BillingCategoryRow> ByCategory { get; set; } = [];
    public List<BillingTrendRow> Trend { get; set; } = [];

    public string? ErrorMessage { get; set; }
    public bool IsConnected { get; set; }
}

public record BillingCategoryRow(string Category, int Volume, decimal Cost);

public record BillingTrendRow(DateTime Date, decimal Cost);

public record BillingCurrencyRow(string Code, decimal Amount);
