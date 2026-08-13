namespace Msgifly.Web.Models.ViewModels;

/// <summary>Backs the Billing/Spend page — per-message cost from Meta's pricing_analytics, since Msgifly has no other visibility into WhatsApp spend.</summary>
public class BillingViewModel
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    public decimal TotalCost { get; set; }
    public int TotalVolume { get; set; }

    public List<BillingCategoryRow> ByCategory { get; set; } = [];
    public List<BillingTrendRow> Trend { get; set; } = [];

    public string? ErrorMessage { get; set; }
    public bool IsConnected { get; set; }
}

public record BillingCategoryRow(string Category, int Volume, decimal Cost);

public record BillingTrendRow(DateTime Date, decimal Cost);
