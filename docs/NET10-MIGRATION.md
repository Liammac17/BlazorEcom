# .NET 10 migration record

Migration date: November 6, 2026.

## Scope and source history

The application started as a .NET Core 3.1 Blazor Server host and seed utility, with .NET Standard 2.0 libraries. The migration retains the exact solution/project layout and established patterns.

The initial GitHub history contained only `.gitattributes`. The existing local application was first imported separately in baseline commit `770f53f` (`Import existing Blazor e-commerce source baseline`). That baseline contains **129 files**: 127 application/solution/assets files plus `.gitattributes` and `.gitignore`. Keeping that import separate makes the framework migration reviewable against the actual original source.

The [codebase review](CODEBASE-REVIEW.md) records the full pre-change review, including its file appendix and the locked Visual Studio index exception.

## Framework and SDK changes

All **11 projects** now target `net10.0`:

| Project | Previous target | New target |
| --- | --- | --- |
| `eShop.Web/eShop.Web.csproj` | `netcoreapp3.1` | `net10.0` |
| `SeedData/SeedData.csproj` | `netcoreapp3.1` | `net10.0` |
| `eShop.CoreBusiness/eShop.CoreBusiness.csproj` | `netstandard2.0` | `net10.0` |
| `eShop.UseCases/eShop.UseCases.csproj` | `netstandard2.0` | `net10.0` |
| `eShop.DataStore.SQL.Dapper/eShop.DataStore.SQL.Dapper.csproj` | `netstandard2.0` | `net10.0` |
| `eShop.ShoppingCart.LocalStorage/eShop.ShoppingCart.LocalStorage.csproj` | `netstandard2.0` | `net10.0` |
| `eShop.StateStore.LocalStorage/eShop.StateStore.LocalStorage.csproj` | `netstandard2.0` | `net10.0` |
| `eShop.Web.AdminPortal/eShop.Web.AdminPortal.csproj` | `netstandard2.0` | `net10.0` |
| `eShop.Web.Common/eShop.Web.Common.csproj` | `netstandard2.0` | `net10.0` |
| `eShop.Web.CustomerPortal/eShop.Web.CustomerPortal.csproj` | `netstandard2.0` | `net10.0` |
| `Plugins/eShop.DataStore.HardCoded/eShop.DataStore.HardCoded.csproj` | `netstandard2.0` | `net10.0` |

The libraries now require .NET 10 consumers; they no longer provide the broad compatibility of a .NET Standard 2.0 target.

Added `global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

This selects a stable .NET 10 SDK even when a newer preview SDK is installed. It allows later installed feature bands within .NET 10 and selected SDK **10.0.403** during validation.

## Razor class library changes

In these four projects:

- `eShop.ShoppingCart.LocalStorage`
- `eShop.Web.AdminPortal`
- `eShop.Web.Common`
- `eShop.Web.CustomerPortal`

Removed the `RazorLangVersion` 3.0 override so the .NET 10 SDK selects the supported Razor compiler. Replaced the explicit 3.1.6 `Microsoft.AspNetCore.Components`, `Microsoft.AspNetCore.Components.Web`, and, where present, `Microsoft.AspNetCore.Components.Authorization` packages with:

```xml
<FrameworkReference Include="Microsoft.AspNetCore.App" />
```

The Razor libraries therefore use the ASP.NET Core shared framework consistently with the host. Their SDK type, project references, component files, static assets, and namespaces are unchanged.

## Dependency changes

| Dependency | Previous version | Migrated version | Location/reason |
| --- | --- | --- | --- |
| `Newtonsoft.Json` | 12.0.3 | 13.0.4 | Core business and local-storage cart; retain JSON serialization format and existing library |
| `Dapper` | 2.0.35 | 2.1.89 | SQL data store; retain existing repository and SQL execution pattern |
| `System.Data.SqlClient` | 4.8.2 | Removed | Replaced by the maintained Microsoft SQL Server provider |
| `Microsoft.Data.SqlClient` | Not present | 7.1.1 | SQL data store |
| `System.Security.Cryptography.Pkcs` | Transitive 9.0.18 after provider update | Direct 10.0.14 | Override the vulnerable transitive resolution from SqlClient |
| `AutoMapper` | 10.0.0 | 16.3.0 | Customer portal; retain existing checkout mapping |
| ASP.NET Core component packages | 3.1.6 | Shared framework | Four Razor class libraries as described above |

The `Pkcs` direct reference addresses audit findings [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) and [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx) reported for the provider's transitive version. It remains explicitly referenced so subsequent restores resolve the patched version.

Versions were checked against NuGet during migration. Review future package upgrades and rerun a transitive vulnerability audit rather than assuming these versions remain current indefinitely.

## SQL provider compatibility

Changed only the provider namespace in `eShop.DataStore.SQL.Dapper/Helper/DataAccess.cs`:

```csharp
using Microsoft.Data.SqlClient;
```

The `IDataAccess` contract, `IDbConnection` usage, disposal, synchronous Dapper methods, parameterized SQL, repositories, and connection-per-operation pattern are retained.

Modern SqlClient encrypts connections by default and validates the SQL Server certificate. Added explicit `Encrypt=True;TrustServerCertificate=True` to the two existing local connection strings:

- `eShop.Web/appsettings.Development.json`
- `SeedData/Program.cs`

This supports the original local Windows-integrated SQL Server setup with its development certificate. Production has no committed connection string and should use `Encrypt=True;TrustServerCertificate=False` with a trusted server certificate. Configuration examples and database prerequisites are in [README.md](../README.md).

No database schema, migration, seed-content, SQL query, or authentication changes were introduced.

## AutoMapper API compatibility

In `eShop.Web.CustomerPortal/Pages/PlaceOrderComponent.razor`:

1. Added `@inject Microsoft.Extensions.Logging.ILoggerFactory loggerFactory`.
2. Passed `loggerFactory` as the second argument to `AutoMapper.MapperConfiguration`.

AutoMapper 15 and later require this constructor argument. The existing inline configuration, `CustomerViewModel` to `Order` mapping, existing destination instance, and checkout handler pattern are preserved.

AutoMapper's current licensing applies to 16.3.0. No license key is committed. A server can supply `AUTOMAPPER_LICENSE_KEY`, which AutoMapper discovers automatically. According to the publisher, a missing key logs a warning and does not disable functionality. See the [upgrade guide](https://docs.automapper.io/en/stable/15.0-Upgrade-Guide.html) and [licensing documentation](https://docs.automapper.io/en/stable/License-configuration.html).

## Repository and documentation changes

Extended `.gitignore` while retaining the original `bin` and `obj` exclusions:

- `.vs/` excludes local Visual Studio data.
- `*.user` and `*.suo` exclude user-specific IDE settings.
- `artifacts/` excludes local publish and validation output.

Added:

- `README.md`: prerequisites, build/run/publish commands, SQL setup, deployment configuration, licensing, and project map.
- `docs/NET10-MIGRATION.md`: this change record and validation results.
- `docs/CODEBASE-REVIEW.md`: complete review coverage, compatibility decisions, existing limitations, and baseline file inventory.

## Preserved structure and patterns

The following remain unchanged:

- The `eShop.Web.sln` solution, all 11 project paths, and all project-reference relationships.
- `Program.cs` and `Startup.cs`, `Host.CreateDefaultBuilder`, and `UseStartup<Startup>`.
- Traditional Blazor Server hosting with `AddServerSideBlazor`, `MapBlazorHub`, `MapFallbackToPage`, `ServerPrerendered`, and `blazor.server.js`.
- Existing dependency-injection registrations and service lifetimes.
- Models, business validation, use-case/plugin interfaces, implementations, repositories, and SQL.
- Customer/admin routes, authorization attributes, cookie scheme, login flow, markup, styling, and navigation.
- Local-storage key `eShop.ShoppingCart`, Newtonsoft JSON serialization, cart state notifications, and existing event patterns.
- Static assets, third-party assets/licenses, sample products, and seed insertion logic.
- The existing `eShop.UseCases/PluginInterfaces/UI/_Host.cshtml` content/publish settings.

## Validation record

Validated on Windows with .NET SDK 10.0.403 and ASP.NET Core runtime 10.0.14:

| Check | Result |
| --- | --- |
| Stable .NET 10 SDK selection | 10.0.403 selected by `global.json` |
| Release solution build | Passed, 0 warnings and 0 errors |
| Release web publish | Passed |
| Solution dependency audit, including transitive packages | No known vulnerable packages reported in any of the 11 projects by the configured NuGet sources |
| Project structure and content invariants | All original project references and content/publish items preserved; solution file and static assets unchanged |
| Development storefront and product detail | Passed against the isolated SQL fixture |
| Cart and quantity updates | Added the fixture product, changed quantity to 2, and verified the $29.98 total |
| Browser cart persistence | Reload retained the item and quantity |
| Checkout validation | Empty submission displayed all five required customer-field errors |
| Checkout and confirmation | Submitted fixture customer data, reached confirmation, and verified the cart was emptied |
| SQL persistence | Verified all five mapped customer fields, product 495, quantity 2, and price 14.99 in SQL Server |
| Admin authentication and authorization | Anonymous admin route showed login; existing demo login and logout passed |
| Order processing | Created order appeared in outstanding orders, was marked processed, then appeared in processed orders; SQL recorded the processing date and admin name |
| Published Production runtime | Started the published application with an environment-provided connection string; product search and empty-result search passed |
| Development and published static assets | All seven checked URLs returned HTTP 200 with JavaScript/CSS content types |
| Independent final migration review | No actionable migration-specific defects found |
| Whitespace verification | `git diff --check` passed |

Commands used for build, publish, and audit:

```powershell
dotnet --version
dotnet restore eShop.Web.sln
dotnet build eShop.Web.sln --configuration Release --no-restore
dotnet publish eShop.Web/eShop.Web.csproj --configuration Release --no-build --output artifacts/publish
dotnet list eShop.Web.sln package --vulnerable --include-transitive
git diff --check
```

The checked asset paths were `/css/site.css`, `/css/bootstrap/bootstrap.min.css`, `/_framework/blazor.server.js`, `/_content/eShop.Web.Common/jsNavigator.js`, and `styles.css` under each of the customer, admin, and local-storage cart libraries' `_content` paths. Browser tests exercised an actual Blazor Server connection in both Development and the published Production application.

The configured local `eShop` database did not exist. Runtime tests used a separate, uniquely named temporary SQL Server database with a minimal schema inferred from the existing models and repository SQL, plus one fixture product. Only that fixture database received test writes. The test hosts were stopped and the fixture database was removed afterward; no existing database was changed. The temporary schema is test scaffolding, not an authoritative production schema.

The repository originally had no automated test projects, and none were added to the solution. The checks above combine build/audit commands, browser interaction, HTTP asset checks, and direct SQL verification. `SeedData` was compiled in the solution build; its insert-only program was not executed against the user's local `eShop` database. Deployment-specific SQL schema, credentials, certificates, HTTPS, and AutoMapper licensing still need to be supplied by the deployment environment as described in the README.

## Primary references

- [Microsoft: migrate ASP.NET Core to .NET 10](https://learn.microsoft.com/en-us/aspnet/core/migration/90-to-100?view=aspnetcore-10.0)
- [Microsoft: SDK selection with global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)
- [Microsoft: Razor class library shared framework references](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/ui-class?view=aspnetcore-10.0)
- [Microsoft: SqlClient encryption and certificate validation](https://learn.microsoft.com/en-us/sql/connect/ado-net/encryption-and-certificate-validation)
- [AutoMapper 16.3.0 on NuGet](https://www.nuget.org/packages/AutoMapper/16.3.0)
