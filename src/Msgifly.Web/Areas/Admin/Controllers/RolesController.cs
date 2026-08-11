using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Msgifly.Web.Authorization;
using Msgifly.Web.Data;
using Msgifly.Web.Extensions;
using Msgifly.Web.Models;
using Msgifly.Web.Models.ViewModels;

namespace Msgifly.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class RolesController : Controller
{
    private const int PageSize = 15;
    private const string SystemRoleName = "Admin";

    private readonly ApplicationDbContext _db;
    private readonly RoleManager<IdentityRole<int>> _roleManager;

    public RolesController(ApplicationDbContext db, RoleManager<IdentityRole<int>> roleManager)
    {
        _db = db;
        _roleManager = roleManager;
    }

    [Authorize(Policy = "role.view")]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var query = _db.Roles.AsNoTracking().OrderBy(r => r.Name).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r => r.Name!.Contains(search));
        }

        var paged = await PagedList<IdentityRole<int>>.CreateAsync(query, page, PageSize);

        var userCounts = new Dictionary<int, int>();
        foreach (var role in paged.Items)
        {
            userCounts[role.Id] = await _db.UserRoles.CountAsync(ur => ur.RoleId == role.Id);
        }

        ViewData["Search"] = search;
        ViewData["UserCounts"] = userCounts;
        ViewData["SystemRoleName"] = SystemRoleName;
        return View(paged);
    }

    [Authorize(Policy = "role.create,role.edit")]
    public async Task<IActionResult> Save(int? id)
    {
        var model = new RoleFormViewModel { AllPermissions = Permissions.All };

        if (id is not null)
        {
            var role = await _roleManager.FindByIdAsync(id.Value.ToString());
            if (role is null || role.Name == SystemRoleName)
            {
                return NotFound();
            }

            var claims = await _roleManager.GetClaimsAsync(role);
            model.Id = role.Id;
            model.Name = role.Name!;
            model.SelectedPermissions = claims
                .Where(c => c.Type == PermissionAuthorizationHandler.PermissionClaimType)
                .Select(c => c.Value)
                .ToList();
        }

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "role.create,role.edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(RoleFormViewModel model)
    {
        model.AllPermissions = Permissions.All;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        IdentityRole<int> role;
        if (model.Id is null)
        {
            if (await _roleManager.RoleExistsAsync(model.Name))
            {
                ModelState.AddModelError(nameof(model.Name), "A role with this name already exists.");
                return View(model);
            }

            role = new IdentityRole<int>(model.Name);
            var createResult = await _roleManager.CreateAsync(role);
            if (!createResult.Succeeded)
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            this.Notify("Role created.");
        }
        else
        {
            var existing = await _roleManager.FindByIdAsync(model.Id.Value.ToString());
            if (existing is null || existing.Name == SystemRoleName)
            {
                return NotFound();
            }

            role = existing;
            role.Name = model.Name;
            await _roleManager.UpdateAsync(role);
            this.Notify("Role updated.");
        }

        var currentClaims = (await _roleManager.GetClaimsAsync(role))
            .Where(c => c.Type == PermissionAuthorizationHandler.PermissionClaimType)
            .ToList();

        foreach (var claim in currentClaims.Where(c => !model.SelectedPermissions.Contains(c.Value)))
        {
            await _roleManager.RemoveClaimAsync(role, claim);
        }

        var existingValues = currentClaims.Select(c => c.Value).ToHashSet();
        foreach (var permission in model.SelectedPermissions.Where(p => !existingValues.Contains(p)))
        {
            await _roleManager.AddClaimAsync(role, new Claim(PermissionAuthorizationHandler.PermissionClaimType, permission));
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = "role.delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null || role.Name == SystemRoleName)
        {
            return NotFound();
        }

        if (await _db.UserRoles.AnyAsync(ur => ur.RoleId == id))
        {
            this.Notify("Can't delete a role that's still assigned to users.", "danger");
            return RedirectToAction(nameof(Index));
        }

        await _roleManager.DeleteAsync(role);
        this.Notify("Role deleted.");
        return RedirectToAction(nameof(Index));
    }
}
