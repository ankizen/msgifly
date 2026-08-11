using Microsoft.AspNetCore.Mvc;

namespace Msgifly.Web.ViewComponents;

/// <summary>
/// Structural chrome (ported from the original's Livewire sidebar-navigation component).
/// Nav items are added incrementally as each later phase introduces the feature they link to.
/// </summary>
public class SidebarNavigationViewComponent : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        return View();
    }
}
