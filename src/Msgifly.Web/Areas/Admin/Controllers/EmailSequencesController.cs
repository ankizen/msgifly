using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.ViewModels;
using Msgifly.Web.Services.Workspaces;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class EmailSequencesController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentWorkspaceAccessor _workspaceAccessor;

    public EmailSequencesController(ApplicationDbContext db, ICurrentWorkspaceAccessor workspaceAccessor)
    {
        _db = db;
        _workspaceAccessor = workspaceAccessor;
    }

    [Authorize(Policy = "email_sequence.view")]
    public async Task<IActionResult> Index(int page = 1)
    {
        var query = _db.EmailSequences.AsNoTracking().OrderByDescending(s => s.CreatedAt);
        var paged = await PagedList<EmailSequence>.CreateAsync(query, page, PageSize);

        var sequenceIds = paged.Items.Select(s => s.Id).ToList();
        var mailCounts = await _db.EmailSequenceMails.AsNoTracking().Where(m => sequenceIds.Contains(m.SequenceId))
            .GroupBy(m => m.SequenceId).Select(g => new { SequenceId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.SequenceId, x => x.Count);
        var activeCounts = await _db.EmailSequenceSubscribers.AsNoTracking()
            .Where(s => sequenceIds.Contains(s.SequenceId) && s.Status == Models.Enums.EmailSequenceSubscriberStatus.Active)
            .GroupBy(s => s.SequenceId).Select(g => new { SequenceId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.SequenceId, x => x.Count);
        ViewData["MailCounts"] = mailCounts;
        ViewData["ActiveCounts"] = activeCounts;

        return View(paged);
    }

    [Authorize(Policy = "email_sequence.create,email_sequence.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        await PopulateOptionsAsync();

        if (id is null)
        {
            return View(new EmailSequenceFormViewModel());
        }

        var sequence = await _db.EmailSequences.FindAsync(id.Value);
        if (sequence is null)
        {
            return NotFound();
        }

        var mails = await _db.EmailSequenceMails.AsNoTracking().Where(m => m.SequenceId == id).OrderBy(m => m.Order).ToListAsync();

        return View(new EmailSequenceFormViewModel
        {
            Id = sequence.Id,
            Name = sequence.Name,
            Status = sequence.Status,
            AutoEnrollListId = sequence.AutoEnrollListId,
            Mails = mails.Select(m => new EmailSequenceMailInput
            {
                Id = m.Id,
                Subject = m.Subject,
                BodyHtml = m.BodyHtml,
                DelayAmount = m.DelayAmount,
                DelayUnit = m.DelayUnit,
            }).ToList(),
        });
    }

    [HttpPost]
    [Authorize(Policy = "email_sequence.create,email_sequence.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmailSequenceFormViewModel model)
    {
        if (model.Mails.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one email to the sequence.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync();
            return View(model);
        }

        EmailSequence sequence;
        if (model.Id is null)
        {
            sequence = new EmailSequence { WorkspaceId = _workspaceAccessor.WorkspaceId!.Value };
            _db.EmailSequences.Add(sequence);
        }
        else
        {
            var existing = await _db.EmailSequences.FindAsync(model.Id.Value);
            if (existing is null)
            {
                return NotFound();
            }

            sequence = existing;
            sequence.UpdatedAt = DateTime.UtcNow;
        }

        sequence.Name = model.Name.Trim();
        sequence.Status = model.Status;
        sequence.AutoEnrollListId = model.AutoEnrollListId;

        await _db.SaveChangesAsync(); // need sequence.Id for mail FKs

        var oldMails = await _db.EmailSequenceMails.Where(m => m.SequenceId == sequence.Id).ToListAsync();
        _db.EmailSequenceMails.RemoveRange(oldMails);

        var order = 0;
        foreach (var mail in model.Mails)
        {
            _db.EmailSequenceMails.Add(new EmailSequenceMail
            {
                SequenceId = sequence.Id,
                Order = order++,
                Subject = mail.Subject,
                BodyHtml = mail.BodyHtml,
                DelayAmount = mail.DelayAmount,
                DelayUnit = mail.DelayUnit,
            });
        }

        await _db.SaveChangesAsync();

        this.Notify(model.Id is null ? "Sequence created." : "Sequence updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "email_sequence.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var sequence = await _db.EmailSequences.FindAsync(id);
        if (sequence is null)
        {
            return NotFound();
        }

        _db.EmailSequences.Remove(sequence);
        await _db.SaveChangesAsync();
        this.Notify("Sequence deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateOptionsAsync()
    {
        ViewData["ListOptions"] = await _db.EmailLists.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem { Value = l.Id.ToString(), Text = l.Name }).ToListAsync();
    }
}
