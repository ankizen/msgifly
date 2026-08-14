using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Services.Tracking;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Jobs;

/// <summary>
/// Runs hourly (registered as a Hangfire recurring job in Program.cs) — re-checks every workspace's
/// configured tracking domain so a working domain that later breaks (cert renewal failure, DNS
/// change) gets flagged without the admin needing to revisit Settings and click "Check now"
/// themselves. Same VerifyAsync the Settings page's own manual check uses — one source of truth.
/// </summary>
public class TrackingDomainVerificationJob
{
    private readonly ApplicationDbContext _db;
    private readonly TrackingDomainVerificationService _verificationService;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public TrackingDomainVerificationJob(ApplicationDbContext db, TrackingDomainVerificationService verificationService, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _verificationService = verificationService;
        _workspaceAccessor = workspaceAccessor;
    }

    public async Task VerifyAllAsync()
    {
        var workspaces = await _db.Workspaces.IgnoreQueryFilters()
            .Where(w => !w.IsArchived && w.TrackingDomain != null)
            .ToListAsync();

        foreach (var workspace in workspaces)
        {
            _workspaceAccessor.WorkspaceId = workspace.Id;
            workspace.TrackingDomainStatus = await _verificationService.VerifyAsync(workspace);
            workspace.TrackingDomainCheckedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}
