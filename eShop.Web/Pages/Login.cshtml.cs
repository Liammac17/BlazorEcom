using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using eShop.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace eShop.Web.Pages
{
    [AllowAnonymous]
    [EnableRateLimiting("admin-sign-in")]
    public sealed class LoginModel : PageModel
    {
        private readonly AdminAuthenticationService authentication;

        public LoginModel(AdminAuthenticationService authentication)
        {
            this.authentication = authentication;
        }

        [BindProperty]
        [Required, StringLength(100)]
        public string UserName { get; set; } = string.Empty;

        [BindProperty]
        [Required, StringLength(256)]
        public string Password { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string ReturnUrl { get; set; } = "/outstandingorders";

        public bool IsConfigured => authentication.IsConfigured;

        public IActionResult OnGet()
        {
            return User.IsInRole(AdminAuthenticationService.Role)
                ? LocalRedirect(SafeReturnUrl())
                : Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!IsConfigured)
            {
                ModelState.AddModelError(string.Empty, "Admin sign-in is not configured.");
                return Page();
            }

            if (!ModelState.IsValid)
                return Page();

            if (!authentication.ValidateCredentials(UserName, Password))
            {
                ModelState.AddModelError(string.Empty, "The username or password is incorrect.");
                Password = string.Empty;
                ModelState.Remove(nameof(Password));
                return Page();
            }

            var issued = DateTimeOffset.UtcNow;
            var expires = issued.Add(AdminAuthenticationService.SessionLifetime);
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, authentication.UserName),
                new Claim(ClaimTypes.Role, AdminAuthenticationService.Role),
                new Claim(AdminAuthenticationService.CredentialVersionClaim, authentication.CredentialVersion),
                new Claim(AdminAuthenticationService.SessionExpiresClaim,
                    expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
            }, AdminAuthenticationService.Scheme);

            await HttpContext.SignInAsync(AdminAuthenticationService.Scheme,
                new ClaimsPrincipal(identity), new AuthenticationProperties
                {
                    IsPersistent = false,
                    IssuedUtc = issued,
                    ExpiresUtc = expires
                });

            return LocalRedirect(SafeReturnUrl());
        }

        private string SafeReturnUrl() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/outstandingorders";
    }
}
