using System;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace eShop.Web.Security
{
    public sealed class AdminAuthenticationService
    {
        public const string Scheme = "MyShop.Admin";
        public const string Policy = "Admin";
        public const string Role = "Administrator";
        public const string CredentialVersionClaim = "myshop:credential-version";
        public const string SessionExpiresClaim = "myshop:session-expires";
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

        private readonly IOptionsMonitor<AdminAuthenticationOptions> options;
        private readonly IPasswordHasher<AdminAuthenticationOptions> passwordHasher;

        public AdminAuthenticationService(
            IOptionsMonitor<AdminAuthenticationOptions> options,
            IPasswordHasher<AdminAuthenticationOptions> passwordHasher)
        {
            this.options = options;
            this.passwordHasher = passwordHasher;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.UserName)
            && !string.IsNullOrWhiteSpace(options.CurrentValue.PasswordHash);

        public string UserName => options.CurrentValue.UserName;

        public string CredentialVersion => Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(options.CurrentValue.UserName + "\0" + options.CurrentValue.PasswordHash)));

        public bool IsSessionValid(ClaimsPrincipal principal, DateTimeOffset now)
        {
            return IsConfigured
                && principal?.Identity?.IsAuthenticated == true
                && principal.IsInRole(Role)
                && string.Equals(principal.Identity.Name, UserName, StringComparison.Ordinal)
                && principal.FindFirst(CredentialVersionClaim)?.Value == CredentialVersion
                && long.TryParse(principal.FindFirst(SessionExpiresClaim)?.Value,
                    NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
                && now.ToUnixTimeSeconds() < expires;
        }

        public bool ValidateCredentials(string userName, string password)
        {
            if (!IsConfigured || string.IsNullOrEmpty(password) || password.Length > 256)
                return false;

            var account = options.CurrentValue;
            try
            {
                // Hash verification is performed even for an unknown username.
                var result = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
                return result != PasswordVerificationResult.Failed
                    && string.Equals(userName?.Trim(), account.UserName, StringComparison.Ordinal);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
