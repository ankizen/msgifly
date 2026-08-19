namespace Msgifly.Web.Models.Entities;

/// <summary>One drip email in an EmailSequence, ordered by Order. Delay is "time since enrollment"
/// (or since the previous mail — resolved by EmailSequenceService), e.g. DelayAmount=1/DelayUnit="days".</summary>
public class EmailSequenceMail
{
    public int Id { get; set; }

    public int SequenceId { get; set; }
    public EmailSequence Sequence { get; set; } = null!;

    public int Order { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;

    public int DelayAmount { get; set; }

    /// <summary>minutes | hours | days — matches WaitStepConfig.Unit's existing plain-string convention.</summary>
    public string DelayUnit { get; set; } = "days";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
