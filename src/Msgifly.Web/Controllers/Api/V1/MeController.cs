using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;

namespace Msgifly.Web.Controllers.Api.V1;

/// <summary>GET /api/v1/me — lets a caller sanity-check its key before wiring up a real integration.</summary>
[ApiController]
[Route("api/v1/me")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public class MeController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public MeController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var keyId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var apiKey = await _db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == keyId);
        if (apiKey is null)
        {
            return Unauthorized();
        }

        return Ok(new
        {
            data = new
            {
                key_name = apiKey.Name,
                key_prefix = apiKey.KeyPrefix,
                scopes = apiKey.ScopesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
                created_at = apiKey.CreatedAt,
            },
        });
    }
}
