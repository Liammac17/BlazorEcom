using System;
using System.Threading.RateLimiting;
using eShop.CoreBusiness.Services;
using eShop.DataStore.SQL.Dapper;
using eShop.StateStore.LocalStorage;
using eShop.UseCases.OrderConfirmationScreen;
using eShop.UseCases.OutstandingOrderScreen;
using eShop.UseCases.PluginInterfaces.DataStore;
using eShop.UseCases.PluginInterfaces.StateStore;
using eShop.UseCases.PluginInterfaces.UI;
using eShop.UseCases.ProcessedOrderScreen;
using eShop.UseCases.ProcessOrderScreen;
using eShop.UseCases.SearchProductScreen;
using eShop.UseCases.ShoppingCartScreen;
using eShop.UseCases.ViewProductScreen;
using eShop.Web.Common.JsInterOp;
using eShop.Web.Data;
using eShop.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace eShop.Web
{
    public class Startup
    {
        private readonly IWebHostEnvironment environment;

        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            this.environment = environment;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDataProtection().SetApplicationName("MyShop");
            services.Configure<AdminAuthenticationOptions>(Configuration.GetSection("AdminAuthentication"));
            services.AddSingleton<IPasswordHasher<AdminAuthenticationOptions>, PasswordHasher<AdminAuthenticationOptions>>();
            services.AddScoped<AdminAuthenticationService>();
            services.AddAuthentication(AdminAuthenticationService.Scheme)
                .AddCookie(AdminAuthenticationService.Scheme, options =>
                {
                    options.Cookie.Name = "MyShop.Admin";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.Cookie.SecurePolicy = environment.IsDevelopment()
                        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
                    options.ExpireTimeSpan = AdminAuthenticationService.SessionLifetime;
                    options.SlidingExpiration = false;
                    options.Events.OnValidatePrincipal = async context =>
                    {
                        var authentication = context.HttpContext.RequestServices.GetRequiredService<AdminAuthenticationService>();
                        if (!authentication.IsSessionValid(context.Principal, DateTimeOffset.UtcNow))
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(AdminAuthenticationService.Scheme);
                        }
                    };
                });
            services.AddAuthorization(options => options.AddPolicy(AdminAuthenticationService.Policy,
                policy => policy.RequireAuthenticatedUser().RequireRole(AdminAuthenticationService.Role)));
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("admin-sign-in", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            });

            services.AddRazorPages();
            services.AddServerSideBlazor();
            services.AddScoped<AuthenticationStateProvider, AdminRevalidatingAuthenticationStateProvider>();
            services.AddSingleton<WeatherForecastService>();
            services.AddTransient<JsNavigator>();
            services.AddScoped<IShoppingCartStateStore, ShoppingCartStateStore>();
            services.AddScoped<IShoppingCart, eShop.ShoppingCart.LocalStorage.ShoppingCart>();
            services.AddTransient<IOrderService, OrderService>();
            services.AddTransient<IDataAccess>(_ => new DataAccess(Configuration.GetConnectionString("Default")));
            services.AddTransient<IProductRepository, ProductRepository>();
            services.AddTransient<IOrderRepository, OrderRepository>();
            services.AddTransient<ISearchProductUseCase, SearchProductUseCase>();
            services.AddTransient<IViewProductUseCase, ViewProductUseCase>();
            services.AddTransient<IAddProductToCartUseCase, AddProductToCartUseCase>();
            services.AddTransient<IDeleteProductUseCase, DeleteProductUseCase>();
            services.AddTransient<IUpdateQuantityUseCase, UpdateQuantityUseCase>();
            services.AddTransient<IViewShoppingCartUseCase, ViewShoppingCartUseCase>();
            services.AddTransient<IPlaceOrderUseCase, PlaceOrderUseCase>();
            services.AddTransient<IViewOrderConfirmationUseCase, ViewOrderConfirmationUseCase>();
            services.AddTransient<IViewOutstandingOrderUseCase, ViewOutstandingOrderUseCase>();
            services.AddTransient<IViewProcessedOrdersUseCase, ViewProcessedOrdersUseCase>();
            services.AddTransient<IViewOrderDetailUseCase, ViewOrderDetailUseCase>();
            services.AddTransient<IProcessOrderUseCase, ProcessOrderUseCase>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}
