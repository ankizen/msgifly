using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.Entities;
using Msgifly.Web.Models.ViewModels;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class UsersController : Controller
{
    private const int PageSize = 15;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<int>> _roleManager;

    public UsersController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<int>> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    [Authorize(Policy = "user.view")]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var query = _db.Users.AsNoTracking().OrderBy(u => u.FirstName).ThenBy(u => u.LastName).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.FirstName.Contains(search) || u.LastName.Contains(search) || u.Email!.Contains(search));
        }

        var paged = await PagedList<ApplicationUser>.CreateAsync(query, page, PageSize);

        // One extra query for role display names rather than N+1 per row.
        var roleNamesByUserId = new Dictionary<int, string>();
        foreach (var user in paged.Items)
        {
            var roles = await _userManager.GetRolesAsync(user);
            roleNamesByUserId[user.Id] = roles.FirstOrDefault() ?? "-";
        }

        var workspaceNamesById = await _db.Workspaces.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name);

        ViewData["Search"] = search;
        ViewData["RoleNames"] = roleNamesByUserId;
        ViewData["WorkspaceNames"] = workspaceNamesById;
        return View(paged);
    }

    [Authorize(Policy = "user.create,user.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        var model = new UserFormViewModel { Active = true };

        if (id is not null)
        {
            var user = await _userManager.FindByIdAsync(id.Value.ToString());
            if (user is null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var roleId = await _db.Roles.Where(r => roles.Contains(r.Name!)).Select(r => (int?)r.Id).FirstOrDefaultAsync();

            model = new UserFormViewModel
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email!,
                Phone = user.Phone,
                IsAdmin = user.IsAdmin,
                Active = user.Active,
                RoleId = roleId,
                WorkspaceId = user.WorkspaceId,
            };
        }

        model.RoleOptions = await GetRoleOptionsAsync();
        model.WorkspaceOptions = await GetWorkspaceOptionsAsync();
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "user.create,user.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(UserFormViewModel model)
    {
        if (model.Id is null && string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(model.Password), "A password is required for a new user.");
        }

        if (!model.IsAdmin && model.WorkspaceId is null)
        {
            // Every non-admin user must be locked to exactly one Workspace going forward — no
            // silent unscoped middle state for newly assigned users (pre-existing users keep
            // today's unscoped behavior until an admin opens and assigns them one, see the
            // WorkspaceUserScopeMiddleware doc comment).
            ModelState.AddModelError(nameof(model.WorkspaceId), "Choose the workspace this user belongs to, or make them a super admin.");
        }

        if (!ModelState.IsValid)
        {
            model.RoleOptions = await GetRoleOptionsAsync();
            model.WorkspaceOptions = await GetWorkspaceOptionsAsync();
            return View(model);
        }

        ApplicationUser user;
        if (model.Id is null)
        {
            user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true,
                FirstName = model.FirstName,
                LastName = model.LastName,
                Phone = model.Phone,
                IsAdmin = model.IsAdmin,
                Active = model.Active,
                WorkspaceId = model.IsAdmin ? null : model.WorkspaceId,
            };

            var createResult = await _userManager.CreateAsync(user, model.Password!);
            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                model.RoleOptions = await GetRoleOptionsAsync();
                model.WorkspaceOptions = await GetWorkspaceOptionsAsync();
                return View(model);
            }

            this.Notify("User created.");
        }
        else
        {
            var existing = await _userManager.FindByIdAsync(model.Id.Value.ToString());
            if (existing is null)
            {
                return NotFound();
            }

            user = existing;
            user.FirstName = model.FirstName;
            user.LastName = model.LastName;
            user.Phone = model.Phone;
            user.IsAdmin = model.IsAdmin;
            user.Active = model.Active;
            user.WorkspaceId = model.IsAdmin ? null : model.WorkspaceId;

            if (user.Email != model.Email)
            {
                await _userManager.SetEmailAsync(user, model.Email);
                await _userManager.SetUserNameAsync(user, model.Email);
            }

            await _userManager.UpdateAsync(user);

            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                await _userManager.ResetPasswordAsync(user, token, model.Password);
            }

            this.Notify("User updated.");
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
        }

        if (model.RoleId is not null)
        {
            var role = await _roleManager.FindByIdAsync(model.RoleId.Value.ToString());
            if (role is not null)
            {
                await _userManager.AddToRoleAsync(user, role.Name!);
            }
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "user.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (currentUserId == id.ToString())
        {
            this.Notify("You can't delete your own account.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        await _userManager.DeleteAsync(user);
        this.Notify("User deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<SelectListItem>> GetRoleOptionsAsync()
    {
        // Mirrors the original: the built-in Admin role is excluded from the picker — the
        // separate IsAdmin flag is the actual superuser toggle (see master doc §8.3).
        return await _db.Roles
            .Where(r => r.Name != "Admin")
            .OrderBy(r => r.Name)
            .Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name })
            .ToListAsync();
    }

    private async Task<List<SelectListItem>> GetWorkspaceOptionsAsync()
    {
        return await _db.Workspaces
            .Where(w => !w.IsArchived)
            .OrderBy(w => w.Id)
            .Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name })
            .ToListAsync();
    }
}
