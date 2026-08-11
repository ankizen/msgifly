namespace Msgifly.Web.Models.Enums;

/// <summary>Bot trigger-matching mode, shared by MessageBot and TemplateBot.</summary>
public enum ReplyType
{
    ExactMatch = 1,
    Contains = 2,
    FirstMessage = 3,
    CatchAll = 4,
}
