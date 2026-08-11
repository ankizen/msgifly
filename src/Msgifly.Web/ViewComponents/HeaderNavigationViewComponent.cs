using Microsoft.AspNetCore.Mvc;

namespace Msgifly.Web.ViewComponents;

public class HeaderNavigationViewComponent : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        return View();
    }
}
