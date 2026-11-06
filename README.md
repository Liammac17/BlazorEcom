# BlazorEcom

A Blazor Server e-commerce application migrated from .NET Core 3.1 / .NET Standard 2.0 to **.NET 10**. The existing solution, project boundaries, repository/use-case patterns, Razor components, and `Program` / `Startup` hosting structure are preserved.

## Requirements

- A stable .NET 10 SDK. [Install .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).
- SQL Server and an existing `eShop` database with the schema expected by the repositories.
- For the default local configuration, Windows authentication to the local SQL Server instance.
- An editor that supports the installed .NET 10 SDK, or use the CLI commands below.

`global.json` selects a stable .NET 10 SDK, starting at 10.0.100 and allowing later .NET 10 feature bands. It selected **10.0.403** on the migration machine and excludes preview SDKs and .NET 11.

## Build and run

From the repository root:

```powershell
dotnet --version
dotnet restore eShop.Web.sln
dotnet build eShop.Web.sln --configuration Release --no-restore
dotnet dev-certs https --trust
dotnet run --project eShop.Web/eShop.Web.csproj
```

The existing launch profile uses Development, with HTTPS at `https://localhost:5001` and HTTP at `http://localhost:5000`.

The default development SQL connection is:

```text
Data Source=(local);Initial Catalog=eShop;Integrated Security=True;Encrypt=True;TrustServerCertificate=True
```

The development setting retains encrypted connections while accepting the local SQL Server certificate. Configure another development server by setting `ConnectionStrings__Default` before running the application.

## Database and seed data

Database creation scripts and migrations are **not included** in the original project. Provide an existing compatible schema before opening database-backed routes:

| Table | Columns used by the code |
| --- | --- |
| `Product` | `ProductId`, `Brand`, `Name`, `Price`, `ImageLink`, `Description` |
| `[Order]` | `OrderId`, `DatePlaced`, `DateProcessing`, `DateProcessed`, `CustomerName`, `CustomerAddress`, `CustomerCity`, `CustomerStateProvince`, `CustomerCountry`, `AdminUser`, `UniqueId` |
| `OrderLineItem` | `LineItemId`, `ProductId`, `OrderId`, `Quantity`, `Price` |

The order insert expects SQL Server to generate `OrderId`. Existing column types, defaults, nullability, and keys must support the repository SQL; the table above describes code usage rather than a complete schema definition.

`SeedData` inserts the existing sample products into the local `eShop` database:

```powershell
dotnet run --project SeedData/SeedData.csproj
```

It does not create tables and is not idempotent. Its SQL omits column names, so the existing `Product` column order must match the insert values. It retains its original hardcoded local connection string and sample content.

## Publish and configure production

```powershell
dotnet publish eShop.Web/eShop.Web.csproj --configuration Release --output artifacts/publish
```

Deploy to a host with the ASP.NET Core 10 runtime, HTTPS, and support for Blazor Server's SignalR connection. Configure the production connection string through the hosting environment; the application has no production connection string checked in.

For example, in a PowerShell session on a Windows host:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__Default = 'Server=sql.example.com;Database=eShop;Integrated Security=True;Encrypt=True;TrustServerCertificate=False'
Set-Location artifacts/publish
dotnet eShop.Web.dll
```

Replace the server and authentication settings for the deployment. Production SQL Server needs a certificate trusted by the application host and matching the server name. The local `TrustServerCertificate=True` setting is confined to the development configuration and the existing local seed utility.

AutoMapper 16.3.0 preserves the existing checkout mapping and introduces the publisher's current licensing terms. Configure a valid license through `AUTOMAPPER_LICENSE_KEY` in the server environment when required by those terms. No license key is checked in. A missing key produces a log warning; AutoMapper documents no feature disabling or licensing network calls. See [AutoMapper licensing](https://docs.automapper.io/en/stable/License-configuration.html).

The original demo authentication controller and its query-string login flow remain in place. Review the existing limitations before production use.

## Solution layout

| Project | Responsibility |
| --- | --- |
| `eShop.Web` | Blazor Server host, routing, authentication, shared layout |
| `eShop.Web.CustomerPortal` | Product browsing, cart, checkout, confirmation |
| `eShop.Web.AdminPortal` | Outstanding and processed orders |
| `eShop.Web.Common` | Shared controls, view models, JavaScript navigation |
| `eShop.CoreBusiness` | Models and order validation |
| `eShop.UseCases` | Application use cases and plugin interfaces |
| `eShop.DataStore.SQL.Dapper` | SQL Server repositories and Dapper access |
| `Plugins/eShop.DataStore.HardCoded` | Existing alternative in-memory repositories |
| `eShop.ShoppingCart.LocalStorage` | Browser local-storage shopping cart |
| `eShop.StateStore.LocalStorage` | Cart state notifications |
| `SeedData` | Existing local sample-product loader |

See [the complete migration record](docs/NET10-MIGRATION.md) for every change and validation results, and [the codebase review](docs/CODEBASE-REVIEW.md) for review coverage and preserved existing issues.
