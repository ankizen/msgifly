namespace Msgifly.Web.Models.Enums;

public enum FlowStatus
{
    /// <summary>Created locally but never submitted to Meta yet, or submitted but not yet published — editable.</summary>
    Draft = 0,

    /// <summary>Live — sendable to customers. Meta locks the Flow JSON from further edits once published.</summary>
    Published = 1,

    /// <summary>Retired — can no longer be sent, kept only for historical reference.</summary>
    Deprecated = 2,
}
