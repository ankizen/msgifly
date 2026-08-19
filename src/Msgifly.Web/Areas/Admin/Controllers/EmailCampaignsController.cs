using System.Text.Json;
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
using Msgifly.Web.Services.Email;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class EmailCampaignsController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly EmailAudienceResolver _audienceResolver;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public EmailCampaignsController(ApplicationDbContext db, EmailAudienceResolver audienceResolver, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _audienceResolver = audienceResolver;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "email_campaign.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.EmailCampaigns.AsNoTracking().OrderByDescending(c => c.CreatedAt);
        var paged = await PagedList<EmailCampaign>.CreateAsync(query, page, PageSize);

        var campaignIds = paged.Items.Select(c => c.Id).ToList();
        var counts = await _db.EmailCampaignRecipients.AsNoTracking()
            .Where(r => campaignIds.Contains(r.CampaignId))
            .GroupBy(r => r.CampaignId)
            .Select(g => new { CampaignId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CampaignId, x => x.Count);
        ViewData["Counts"] = counts;

        return View(paged);
    }

    [Authorize(Policy = "email_campaign.create,email_campaign.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        await PopulateOptionsAsync();

        if (id is null)
        {
            var defaultConnection = await _db.EmailSmtpConnections.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault);
            return View(new EmailCampaignFormViewModel
            {
                FromEmail = defaultConnection?.FromEmail ?? string.Empty,
                FromName = defaultConnection?.FromName ?? string.Empty,
            });
        }

        var campaign = await _db.EmailCampaigns.FindAsync(id.Value);
        if (campaign is null)
        {
            return NotFound();
        }

        if (campaign.Status != EmailCampaignStatus.Draft)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(new EmailCampaignFormViewModel
        {
            Id = campaign.Id,
            Name = campaign.Name,
            FromName = campaign.FromName,
            FromEmail = campaign.FromEmail,
            Subject = campaign.Subject,
            BodyHtml = campaign.BodyHtml,
            SendNow = campaign.SendNow,
            ScheduledAt = campaign.ScheduledAt,
            SelectAll = campaign.SelectAll,
            IncludeListIds = ParseIds(campaign.IncludeListIdsJson),
            ExcludeListIds = ParseIds(campaign.ExcludeListIdsJson),
            IncludeTagIds = ParseIds(campaign.IncludeTagIdsJson),
            ExcludeTagIds = ParseIds(campaign.ExcludeTagIdsJson),
        });
    }

    [HttpPost]
    [Authorize(Policy = "email_campaign.create,email_campaign.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailCampaignFormViewModel model)
    {
        if (!model.SendNow && model.ScheduledAt is null)
        {
            ModelState.AddModelError(nameof(model.ScheduledAt), "Choose a send time, or send now.");
        }

        if (!model.SelectAll && model.IncludeListIds.Count == 0 && model.IncludeTagIds.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose \"All subscribers\", or pick at least one list/tag to target.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync();
            return View(model);
        }

        EmailCampaign campaign;
        bool isNew = model.Id is null;
        if (isNew)
        {
            campaign = new EmailCampaign { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value };
            _db.EmailCampaigns.Add(campaign);
        }
        else
        {
            var existing = await _db.EmailCampaigns.FindAsync(model.Id!.Value);
            if (existing is null || existing.Status != EmailCampaignStatus.Draft)
            {
                return NotFound();
            }

            campaign = existing;
            campaign.UpdatedAt = DateTime.UtcNow;
        }

        campaign.Name = model.Name;
        campaign.FromName = model.FromName;
        campaign.FromEmail = model.FromEmail;
        campaign.Subject = model.Subject;
        campaign.BodyHtml = model.BodyHtml;
        campaign.SendNow = model.SendNow;
        campaign.ScheduledAt = model.SendNow ? null : model.ScheduledAt;
        campaign.SelectAll = model.SelectAll;
        campaign.IncludeListIdsJson = SerializeIds(model.IncludeListIds);
        campaign.ExcludeListIdsJson = SerializeIds(model.ExcludeListIds);
        campaign.IncludeTagIdsJson = SerializeIds(model.IncludeTagIds);
        campaign.ExcludeTagIdsJson = SerializeIds(model.ExcludeTagIds);
        campaign.Status = EmailCampaignStatus.Scheduled;

        await _db.SaveChangesAsync(); // need campaign.Id for recipient FKs

        // Recipients are materialized right now, at save time — not lazily by the dispatch cron —
        // mirroring CampaignsController/CampaignDetail's exact precedent.
        if (!isNew)
        {
            var oldRecipients = await _db.EmailCampaignRecipients.Where(r => r.CampaignId == campaign.Id).ToListAsync();
            _db.EmailCampaignRecipients.RemoveRange(oldRecipients);
        }

        var subscriberIds = await _audienceResolver.ResolveSubscriberIdsAsync(campaign);
        var recipients = subscriberIds.Select(subscriberId => new EmailCampaignRecipient
        {
            CampaignId = campaign.Id,
            SubscriberId = subscriberId,
            Status = EmailCampaignRecipientStatus.Pending,
            TrackingToken = Guid.NewGuid().ToString("N"),
        }).ToList();
        _db.EmailCampaignRecipients.AddRange(recipients);
        await _db.SaveChangesAsync();

        this.Notify($"Campaign \"{campaign.Name}\" scheduled for {recipients.Count} recipient(s).");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = "email_campaign.view")]
    public async Task<IActionResult> Details(int id, int page = 1)
    {
        var campaign = await _db.EmailCampaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (campaign is null)
        {
            return NotFound();
        }

        var query = _db.EmailCampaignRecipients.AsNoTracking().Include(r => r.Subscriber).Where(r => r.CampaignId == id).OrderByDescending(r => r.UpdatedAt);

        var counts = await _db.EmailCampaignRecipients.AsNoTracking().Where(r => r.CampaignId == id)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Pending = g.Count(r => r.Status == EmailCampaignRecipientStatus.Pending),
                Sent = g.Count(r => r.Status == EmailCampaignRecipientStatus.Sent),
                Failed = g.Count(r => r.Status == EmailCampaignRecipientStatus.Failed),
                Opened = g.Count(r => r.IsOpened),
                Clicked = g.Count(r => r.IsClicked),
                Unsubscribed = g.Count(r => r.IsUnsubscribed),
            })
            .FirstOrDefaultAsync();

        ViewData["Campaign"] = campaign;
        ViewData["Pending"] = counts?.Pending ?? 0;
        ViewData["Sent"] = counts?.Sent ?? 0;
        ViewData["Failed"] = counts?.Failed ?? 0;
        ViewData["Opened"] = counts?.Opened ?? 0;
        ViewData["Clicked"] = counts?.Clicked ?? 0;
        ViewData["Unsubscribed"] = counts?.Unsubscribed ?? 0;

        return View(await PagedList<EmailCampaignRecipient>.CreateAsync(query, page, PageSize));
    }

    [HttpPost]
    [Authorize(Policy = "email_campaign.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(int id)
    {
        var campaign = await _db.EmailCampaigns.FindAsync(id);
        if (campaign is null)
        {
            return NotFound();
        }

        campaign.Status = EmailCampaignStatus.Paused;
        await _db.SaveChangesAsync();
        this.Notify("Campaign paused.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "email_campaign.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resume(int id)
    {
        var campaign = await _db.EmailCampaigns.FindAsync(id);
        if (campaign is null)
        {
            return NotFound();
        }

        campaign.Status = EmailCampaignStatus.Sending;
        await _db.SaveChangesAsync();
        this.Notify("Campaign resumed.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "email_campaign.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var campaign = await _db.EmailCampaigns.FindAsync(id);
        if (campaign is null)
        {
            return NotFound();
        }

        _db.EmailCampaigns.Remove(campaign);
        await _db.SaveChangesAsync();
        this.Notify("Campaign deleted.");
        return RedirectToAction(nameof(Index));
    }

    private static List<int> ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? SerializeIds(List<int> ids) => ids.Count == 0 ? null : JsonSerializer.Serialize(ids);

    private async Task PopulateOptionsAsync()
    {
        ViewData["ListOptions"] = await _db.EmailLists.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem { Value = l.Id.ToString(), Text = l.Name }).ToListAsync();
        ViewData["TagOptions"] = await _db.EmailTags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name }).ToListAsync();
    }
}
