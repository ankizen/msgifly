using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Campaigns;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class CampaignsController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;

    public CampaignsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [Authorize(Policy = "campaigns.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.Campaigns.AsNoTracking().OrderByDescending(c => c.CreatedAt);
        var paged = await PagedList<Campaign>.CreateAsync(query, page, PageSize);

        var templateNames = await _db.WhatsappTemplates.AsNoTracking()
            .Where(t => t.MetaTemplateId != null)
            .ToDictionaryAsync(t => t.MetaTemplateId!, t => t.TemplateName);

        var campaignIds = paged.Items.Select(c => c.Id).ToList();
        var counts = await _db.CampaignDetails.AsNoTracking()
            .Where(d => campaignIds.Contains(d.CampaignId))
            .GroupBy(d => new { d.CampaignId, d.Status })
            .Select(g => new { g.Key.CampaignId, g.Key.Status, Count = g.Count() })
            .ToListAsync();

        var items = paged.Items.Select(c => new CampaignListItem
        {
            Id = c.Id,
            Name = c.Name,
            RelType = c.RelType,
            TemplateName = c.TemplateId is not null && templateNames.TryGetValue(c.TemplateId, out var name) ? name : c.TemplateId,
            IsSent = c.IsSent,
            PauseCampaign = c.PauseCampaign,
            ScheduledSendTime = c.ScheduledSendTime,
            TotalCount = counts.Where(x => x.CampaignId == c.Id).Sum(x => x.Count),
            SentCount = counts.Where(x => x.CampaignId == c.Id && x.Status == CampaignDetailStatus.Sent).Sum(x => x.Count),
            FailedCount = counts.Where(x => x.CampaignId == c.Id && x.Status == CampaignDetailStatus.Failed).Sum(x => x.Count),
        }).ToList();

        return View(new PagedList<CampaignListItem>(items, paged.TotalCount, paged.PageIndex, PageSize));
    }

    [Authorize(Policy = "campaigns.create,campaigns.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        var model = new CampaignFormViewModel();

        if (id is not null)
        {
            var campaign = await _db.Campaigns.FindAsync(id.Value);
            if (campaign is null)
            {
                return NotFound();
            }

            if (campaign.IsSent)
            {
                return RedirectToAction(nameof(Details), new { id });
            }

            model = new CampaignFormViewModel
            {
                Id = campaign.Id,
                Name = campaign.Name,
                RelType = campaign.RelType,
                TemplateId = campaign.TemplateId ?? string.Empty,
                SendNow = campaign.SendNow,
                ScheduledSendTime = campaign.ScheduledSendTime,
                SelectAll = campaign.SelectAll,
                HeaderMediaUrl = campaign.FileName,
            };

            FillParamSlots(model.HeaderParams, campaign.HeaderParamsJson);
            FillParamSlots(model.BodyParams, campaign.BodyParamsJson);
        }

        await PopulateOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "campaigns.create,campaigns.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CampaignFormViewModel model)
    {
        if (!model.SelectAll && model.SelectedContactIds.Count == 0)
        {
            ModelState.AddModelError(nameof(model.SelectedContactIds), "Pick at least one contact, or choose \"All matching contacts\".");
        }

        if (!model.SendNow && model.ScheduledSendTime is null)
        {
            ModelState.AddModelError(nameof(model.ScheduledSendTime), "Choose a send time, or send now.");
        }

        var template = await _db.WhatsappTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MetaTemplateId == model.TemplateId && t.Status == TemplateStatus.Approved);
        if (template is null)
        {
            ModelState.AddModelError(nameof(model.TemplateId), "Choose an approved template.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model);
            return View(model);
        }

        // Trim (not filter!) to exactly the template's param counts — {{1}}, {{2}}... positions
        // must line up, so an empty middle slot still needs to hold its place in the list.
        var headerParamCount = string.Equals(template!.HeaderFormat, "TEXT", StringComparison.OrdinalIgnoreCase) ? template.HeaderParamsCount : 0;

        var campaign = new Campaign
        {
            Name = model.Name,
            RelType = model.RelType,
            TemplateId = model.TemplateId,
            SendNow = model.SendNow,
            ScheduledSendTime = model.SendNow ? null : model.ScheduledSendTime,
            SelectAll = model.SelectAll,
            FileName = model.HeaderMediaUrl,
            HeaderParamsJson = SerializeSlots(model.HeaderParams, headerParamCount),
            BodyParamsJson = SerializeSlots(model.BodyParams, template.BodyParamsCount),
        };

        if (model.SelectAll)
        {
            campaign.FilterJson = System.Text.Json.JsonSerializer.Serialize(new { model.FilterStatusId, model.FilterSourceId });
        }

        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        var recipientCount = await CreateCampaignDetailsAsync(campaign, model);
        this.Notify($"Campaign \"{campaign.Name}\" created for {recipientCount} recipient(s).");

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = "campaigns.show_campaign")]
    public async Task<IActionResult> Details(int id, int page = 1)
    {
        var campaign = await _db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (campaign is null)
        {
            return NotFound();
        }

        var detailsQuery = _db.CampaignDetails.AsNoTracking()
            .Include(d => d.Contact)
            .Where(d => d.CampaignId == id)
            .OrderByDescending(d => d.UpdatedAt);

        var counts = await _db.CampaignDetails.AsNoTracking()
            .Where(d => d.CampaignId == id)
            .GroupBy(d => 1)
            .Select(g => new
            {
                Pending = g.Count(d => d.Status == CampaignDetailStatus.Pending),
                Sent = g.Count(d => d.Status == CampaignDetailStatus.Sent),
                Failed = g.Count(d => d.Status == CampaignDetailStatus.Failed),
                Delivered = g.Count(d => d.DeliveryStatus == MessageDeliveryStatus.Delivered || d.DeliveryStatus == MessageDeliveryStatus.Read),
                Read = g.Count(d => d.DeliveryStatus == MessageDeliveryStatus.Read),
            })
            .FirstOrDefaultAsync();

        var model = new CampaignDetailsViewModel
        {
            CampaignId = campaign.Id,
            CampaignName = campaign.Name,
            IsSent = campaign.IsSent,
            PauseCampaign = campaign.PauseCampaign,
            PendingCount = counts?.Pending ?? 0,
            SentCount = counts?.Sent ?? 0,
            FailedCount = counts?.Failed ?? 0,
            DeliveredCount = counts?.Delivered ?? 0,
            ReadCount = counts?.Read ?? 0,
            Details = await PagedList<CampaignDetail>.CreateAsync(detailsQuery, page, PageSize),
        };

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "campaigns.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(int id)
    {
        var campaign = await _db.Campaigns.FindAsync(id);
        if (campaign is null)
        {
            return NotFound();
        }

        campaign.PauseCampaign = true;
        await _db.SaveChangesAsync();
        this.Notify("Campaign paused.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "campaigns.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resume(int id)
    {
        var campaign = await _db.Campaigns.FindAsync(id);
        if (campaign is null)
        {
            return NotFound();
        }

        campaign.PauseCampaign = false;
        await _db.SaveChangesAsync();
        this.Notify("Campaign resumed.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "campaigns.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryFailed(int id)
    {
        var campaign = await _db.Campaigns.FindAsync(id);
        if (campaign is null)
        {
            return NotFound();
        }

        var failedDetails = await _db.CampaignDetails
            .Where(d => d.CampaignId == id && d.Status == CampaignDetailStatus.Failed)
            .ToListAsync();

        foreach (var detail in failedDetails)
        {
            detail.Status = CampaignDetailStatus.Pending;
            detail.ResponseMessage = null;
        }

        campaign.IsSent = false;
        await _db.SaveChangesAsync();

        this.Notify($"{failedDetails.Count} failed message(s) queued for retry.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "campaigns.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var campaign = await _db.Campaigns.FindAsync(id);
        if (campaign is null)
        {
            return NotFound();
        }

        _db.Campaigns.Remove(campaign);
        await _db.SaveChangesAsync();
        this.Notify("Campaign deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<int> CreateCampaignDetailsAsync(Campaign campaign, CampaignFormViewModel model)
    {
        var query = _db.Contacts.Where(c => c.Type == campaign.RelType && c.IsEnabled);

        if (model.SelectAll)
        {
            if (model.FilterStatusId is not null)
            {
                query = query.Where(c => c.StatusId == model.FilterStatusId);
            }

            if (model.FilterSourceId is not null)
            {
                query = query.Where(c => c.SourceId == model.FilterSourceId);
            }
        }
        else
        {
            query = query.Where(c => model.SelectedContactIds.Contains(c.Id));
        }

        var contacts = await query.ToListAsync();

        var details = contacts.Select(c => new CampaignDetail
        {
            CampaignId = campaign.Id,
            ContactId = c.Id,
            Status = CampaignDetailStatus.Pending,
        }).ToList();

        _db.CampaignDetails.AddRange(details);
        await _db.SaveChangesAsync();

        return details.Count;
    }

    private static void FillParamSlots(CampaignParamInput[] slots, string? json)
    {
        var saved = CampaignParamResolver.ParseList(json);
        for (var i = 0; i < slots.Length && i < saved.Count; i++)
        {
            slots[i] = new CampaignParamInput { Source = saved[i].Source, StaticValue = saved[i].StaticValue };
        }
    }

    /// <summary>
    /// Takes exactly <paramref name="count"/> slots (padding with empty static values if the
    /// form posted fewer) — positions must match the template's {{1}}, {{2}}... placeholders,
    /// so slots can't just be filtered out when empty.
    /// </summary>
    private static string SerializeSlots(CampaignParamInput[] slots, int count)
    {
        var result = new List<CampaignParam>(count);
        for (var i = 0; i < count; i++)
        {
            var slot = i < slots.Length ? slots[i] : null;
            result.Add(new CampaignParam { Source = slot?.Source ?? ParamSourceType.StaticText, StaticValue = slot?.StaticValue });
        }

        return CampaignParamResolver.Serialize(result);
    }

    private async Task PopulateOptionsAsync(CampaignFormViewModel model)
    {
        model.TemplateOptions = await _db.WhatsappTemplates.AsNoTracking()
            .Where(t => t.Status == TemplateStatus.Approved && t.MetaTemplateId != null)
            .OrderBy(t => t.TemplateName)
            .Select(t => new TemplateOption(t.MetaTemplateId!, t.TemplateName, t.HeaderFormat, t.HeaderParamsCount, t.BodyParamsCount, t.FooterParamsCount))
            .ToListAsync();

        model.StatusOptions = await _db.Statuses.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync();

        model.SourceOptions = await _db.Sources.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync();

        model.ContactOptions = await _db.Contacts.AsNoTracking().OrderBy(c => c.FirstName)
            .Select(c => new ContactOption(c.Id, c.FirstName + " " + c.LastName + " (" + c.Type + ") - " + c.Phone))
            .ToListAsync();
    }
}
