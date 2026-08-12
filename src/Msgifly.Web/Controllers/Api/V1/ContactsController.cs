using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Authorization;
using Msgifly.Web.Data;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.Enums;
using Msgifly.Web.Services.ApiKeys;

namespace Msgifly.Web.Controllers.Api.V1;

[ApiController]
[Route("api/v1/contacts")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public class ContactsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ContactsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? phone, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        if (!User.HasApiScope(ApiScopes.ContactsRead))
        {
            return Forbid();
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);

        var query = _db.Contacts.AsNoTracking().OrderByDescending(c => c.CreatedAt).AsQueryable();
        if (!string.IsNullOrWhiteSpace(phone))
        {
            query = query.Where(c => c.Phone.Contains(phone));
        }

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return Ok(new
        {
            data = items.Select(ToDto),
            meta = new { page, page_size = pageSize, total },
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (!User.HasApiScope(ApiScopes.ContactsRead))
        {
            return Forbid();
        }

        var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return contact is null ? NotFound(new { error = "not_found", message = "Contact not found." }) : Ok(new { data = ToDto(contact) });
    }

    public record CreateContactRequest(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("phone")] string Phone,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("company")] string? Company,
        [property: JsonPropertyName("status_id")] int? StatusId,
        [property: JsonPropertyName("source_id")] int? SourceId);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateContactRequest request)
    {
        if (!User.HasApiScope(ApiScopes.ContactsWrite))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { error = "bad_request", message = "first_name and phone are required." });
        }

        var statusId = request.StatusId ?? await _db.Statuses.Select(s => (int?)s.Id).FirstOrDefaultAsync();
        var sourceId = request.SourceId ?? await _db.Sources.Select(s => (int?)s.Id).FirstOrDefaultAsync();
        if (statusId is null || sourceId is null)
        {
            return BadRequest(new { error = "bad_request", message = "No default Status/Source configured — pass status_id and source_id explicitly." });
        }

        var contact = new Contact
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName?.Trim() ?? string.Empty,
            Phone = request.Phone.Trim(),
            Email = request.Email,
            Company = request.Company,
            Type = ContactType.Lead,
            StatusId = statusId.Value,
            SourceId = sourceId.Value,
            IsEnabled = true,
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = contact.Id }, new { data = ToDto(contact) });
    }

    public record UpdateContactRequest(
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("company")] string? Company);

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateContactRequest request)
    {
        if (!User.HasApiScope(ApiScopes.ContactsWrite))
        {
            return Forbid();
        }

        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id);
        if (contact is null)
        {
            return NotFound(new { error = "not_found", message = "Contact not found." });
        }

        if (request.FirstName is not null) contact.FirstName = request.FirstName;
        if (request.LastName is not null) contact.LastName = request.LastName;
        if (request.Email is not null) contact.Email = request.Email;
        if (request.Company is not null) contact.Company = request.Company;
        contact.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { data = ToDto(contact) });
    }

    private static object ToDto(Contact c) => new
    {
        id = c.Id,
        first_name = c.FirstName,
        last_name = c.LastName,
        phone = c.Phone,
        email = c.Email,
        company = c.Company,
        type = c.Type.ToString(),
        status_id = c.StatusId,
        source_id = c.SourceId,
        created_at = c.CreatedAt,
        updated_at = c.UpdatedAt,
    };
}
