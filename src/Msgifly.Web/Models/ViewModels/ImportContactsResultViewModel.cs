namespace Msgifly.Web.Models.ViewModels;

public class ImportContactsResultViewModel
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = [];
}
