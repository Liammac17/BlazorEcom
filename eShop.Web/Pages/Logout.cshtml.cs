using System.Threading.Tasks;
using eShop.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace eShop.Web.Pages
{
    [Authorize(Policy = AdminAuthenticationService.Policy)]
    public sealed class LogoutModel : PageModel
    {
        public async Task<IActionResult> OnPostAsync()
        {
            await HttpContext.SignOutAsync(AdminAuthenticationService.Scheme);
            return LocalRedirect("/");
        }
    }
}
