namespace Msgifly.Web.Services.Workspaces;

/// <summary>
/// The tenant-scoping key for the whole request/job: every EF Core query filter (see
/// ApplicationDbContext.OnModelCreating) checks entities' WorkspaceId against this value.
///
/// Backed by AsyncLocal rather than a scoped HttpContext-bound service so the same mechanism
/// works for both web requests (set by WorkspaceResolutionMiddleware from a cookie) and Hangfire
/// background jobs (which run outside any HttpContext and must set it explicitly at the top of
/// the job method, bootstrapped from the entity they were scheduled for — see AutomationEngine
/// .ResumeWaitAsync / CampaignMessageJob.SendMessageAsync for the pattern). Registered as a
/// singleton; AsyncLocal itself keeps values isolated per async flow so concurrent
/// requests/jobs never see each other's workspace.
/// </summary>
public interface ICurrentWorkspaceAccessor
{
    int? WorkspaceId { get; set; }
}

public class CurrentWorkspaceAccessor : ICurrentWorkspaceAccessor
{
    private static readonly AsyncLocal<int?> Holder = new();

    public int? WorkspaceId
    {
        get => Holder.Value;
        set => Holder.Value = value;
    }
}
