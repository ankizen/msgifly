using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.Campaigns;
using Msgifly.Web.Services.WhatsApp;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Jobs;

/// <summary>
/// Sends one campaign message to one recipient — enqueued per-recipient by CampaignDispatchJob,
/// the equivalent of the original's SendCampaignMessageJob (master doc §5.2). WhatsAppService
/// already turns Graph API errors into a WhatsAppResult rather than throwing, so a rejected send
/// (bad number, disallowed template params, etc.) is recorded as Failed without triggering
/// Hangfire's automatic retry — retrying those wouldn't help. Genuine unhandled exceptions (e.g.
/// a DB blip) still propagate and get Hangfire's default retry behavior as a safety net.
/// </summary>
public class CampaignMessageJob
{
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;
    private readonly ILogger<CampaignMessageJob> _logger;

    public CampaignMessageJob(ApplicationDbContext db, IWhatsAppService whatsAppService, ICurrentWorkspaceAccessor workspaceAccessor, ILogger<CampaignMessageJob> logger)
    {
        _db = db;
        _whatsAppService = whatsAppService;
        _workspaceAccessor = workspaceAccessor;
        _logger = logger;
    }

    public async Task SendMessageAsync(int campaignDetailId)
    {
        // No HttpContext here either — bootstrap from the detail's own Campaign before any
        // filtered query runs, same pattern as AutomationEngine.ResumeWaitAsync.
        var detailWorkspaceId = await _db.CampaignDetails.IgnoreQueryFilters()
            .Where(d => d.Id == campaignDetailId)
            .Select(d => (int?)d.Campaign.WorkspaceId)
            .FirstOrDefaultAsync();
        if (detailWorkspaceId is null)
        {
            _logger.LogWarning("CampaignDetail {Id} no longer exists; skipping.", campaignDetailId);
            return;
        }

        _workspaceAccessor.WorkspaceId = detailWorkspaceId;

        var detail = await _db.CampaignDetails
            .Include(d => d.Campaign)
            .Include(d => d.Contact)
            .FirstOrDefaultAsync(d => d.Id == campaignDetailId);

        if (detail is null || detail.Contact is null)
        {
            _logger.LogWarning("CampaignDetail {Id} or its contact no longer exists; skipping.", campaignDetailId);
            return;
        }

        if (detail.Campaign.PauseCampaign)
        {
            return; // left Pending — picked up again once the campaign is resumed
        }

        var template = await _db.WhatsappTemplates.FirstOrDefaultAsync(t => t.MetaTemplateId == detail.Campaign.TemplateId);
        if (template is null)
        {
            detail.Status = CampaignDetailStatus.Failed;
            detail.ResponseMessage = "The template used by this campaign is no longer available.";
            await _db.SaveChangesAsync();
            return;
        }

        var headerParams = CampaignParamResolver.ResolveAll(detail.Campaign.HeaderParamsJson, detail.Contact);
        var bodyParams = CampaignParamResolver.ResolveAll(detail.Campaign.BodyParamsJson, detail.Contact);

        var request = new TemplateSendRequest
        {
            TemplateName = template.TemplateName,
            Language = template.Language,
            HeaderFormat = template.HeaderFormat,
            HeaderText = headerParams.Count > 0 ? headerParams[0] : null,
            HeaderMediaUrl = detail.Campaign.FileName,
            BodyParams = bodyParams,
        };

        var result = await _whatsAppService.SendTemplateMessageAsync(detail.Contact.Phone, request);

        detail.HeaderMessage = TemplateMessageRenderer.RenderText(template.HeaderText, headerParams);
        detail.BodyMessage = TemplateMessageRenderer.RenderText(template.BodyText, bodyParams);
        detail.FooterMessage = template.FooterText;
        detail.UpdatedAt = DateTime.UtcNow;

        if (result.Success)
        {
            detail.Status = CampaignDetailStatus.Sent;
            detail.DeliveryStatus = MessageDeliveryStatus.Sent;
            detail.WhatsappMessageId = result.Data;
            detail.ResponseMessage = null;
            // Stamped here rather than waiting on Meta's async "sent" status callback — that
            // webhook usually follows within seconds but isn't guaranteed to, and "sent" is
            // already a fact at this point (Meta accepted the send request).
            detail.SentAt ??= DateTime.UtcNow;
        }
        else
        {
            detail.Status = CampaignDetailStatus.Failed;
            detail.DeliveryStatus = MessageDeliveryStatus.Failed;
            detail.ResponseMessage = result.ErrorMessage;
            detail.FailedAt ??= DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}
