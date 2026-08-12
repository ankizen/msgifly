namespace Msgifly.Web.Models.Enums;

public enum TemplateStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,

    /// <summary>Created locally but never submitted to Meta yet.</summary>
    Draft = 3,

    /// <summary>Meta paused this template for quality reasons — editable/resubmittable, same as Rejected.</summary>
    Paused = 4,
}
