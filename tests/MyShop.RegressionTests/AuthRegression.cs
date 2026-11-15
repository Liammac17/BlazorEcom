using System;
using System.Globalization;
using System.Security.Claims;
using eShop.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

internal static class AuthRegression
{
    public static void Run()
    {
        var account = new AdminAuthenticationOptions { UserName = "owner" };
        var hasher = new PasswordHasher<AdminAuthenticationOptions>();
        account.PasswordHash = hasher.HashPassword(account, "A test-only phrase, 42!");
        var monitor = new MutableOptionsMonitor(account);
        var authentication = new AdminAuthenticationService(monitor, hasher);

        Require(authentication.IsConfigured, "A configured account must be available.");
        Require(authentication.ValidateCredentials("owner", "A test-only phrase, 42!"), "The configured password must verify.");
        Require(!authentication.ValidateCredentials("another-user", "A test-only phrase, 42!"), "A valid password must not authenticate another username.");
        Require(!authentication.ValidateCredentials("owner", "incorrect"), "An incorrect password must be rejected.");
        Require(!authentication.ValidateCredentials("owner", new string('x', 257)), "Overlong passwords must be rejected before hashing.");

        var issued = DateTimeOffset.UtcNow;
        var validSession = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, account.UserName),
            new Claim(ClaimTypes.Role, AdminAuthenticationService.Role),
            new Claim(AdminAuthenticationService.CredentialVersionClaim, authentication.CredentialVersion),
            new Claim(AdminAuthenticationService.SessionExpiresClaim,
                issued.Add(AdminAuthenticationService.SessionLifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        }, AdminAuthenticationService.Scheme));
        Require(authentication.IsSessionValid(validSession, issued), "An unexpired session with matching credentials and admin role must remain valid.");
        Require(!authentication.IsSessionValid(validSession, issued.Add(AdminAuthenticationService.SessionLifetime)), "An open circuit's session must be invalid after its eight-hour expiry.");
        Require(!authentication.IsSessionValid(new ClaimsPrincipal(new ClaimsIdentity()), issued), "Anonymous principals cannot become administrator sessions.");

        var version = authentication.CredentialVersion;
        account.PasswordHash = hasher.HashPassword(account, "A replacement test-only phrase, 43!");
        Require(authentication.CredentialVersion != version, "Rotating the password hash must invalidate existing cookie fingerprints.");
        Require(!authentication.IsSessionValid(validSession, issued), "An open circuit's old credential fingerprint must fail after password rotation.");
        Require(!authentication.ValidateCredentials("owner", "A test-only phrase, 42!"), "The previous password must stop working after rotation.");
        Require(authentication.ValidateCredentials("owner", "A replacement test-only phrase, 43!"), "The replacement password must verify.");

        account.PasswordHash = "not-a-valid-base64-password-hash";
        Require(!authentication.ValidateCredentials("owner", "anything"), "Malformed configuration must fail closed without throwing.");
        monitor.CurrentValue = new AdminAuthenticationOptions();
        Require(!authentication.IsConfigured, "Missing configuration must not create a default account.");
        Require(!authentication.ValidateCredentials("admin", "adminadmin"), "The former embedded demonstration account must not work.");

        Console.WriteLine("PASS administrator password verification, configuration, and credential rotation");
    }

    private static void Require(bool condition, string message)
    {
        Regression.Check(condition, message);
    }

    private sealed class MutableOptionsMonitor : IOptionsMonitor<AdminAuthenticationOptions>
    {
        public MutableOptionsMonitor(AdminAuthenticationOptions value) => CurrentValue = value;
        public AdminAuthenticationOptions CurrentValue { get; set; }
        public AdminAuthenticationOptions Get(string name) => CurrentValue;
        public IDisposable OnChange(Action<AdminAuthenticationOptions, string> listener) => new EmptySubscription();
        private sealed class EmptySubscription : IDisposable { public void Dispose() { } }
    }
}
