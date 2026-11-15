using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Logging;

namespace eShop.Web.Security
{
    public sealed class AdminRevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
    {
        private readonly AdminAuthenticationService authentication;

        public AdminRevalidatingAuthenticationStateProvider(
            ILoggerFactory loggerFactory, AdminAuthenticationService authentication) : base(loggerFactory)
        {
            this.authentication = authentication;
        }

        protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

        protected override Task<bool> ValidateAuthenticationStateAsync(
            AuthenticationState authenticationState, CancellationToken cancellationToken)
            => Task.FromResult(authentication.IsSessionValid(authenticationState.User, DateTimeOffset.UtcNow));
    }
}
