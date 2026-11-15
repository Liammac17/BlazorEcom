# MyShop

A .NET 10 Blazor Server beauty storefront built with Clean Architecture. MyShop includes a responsive catalog, browser-persisted shopping bag, guest order placement and an administrator order desk.

The existing `eShop.*` project/namespace names and `eShop` database name remain the technical identifiers. The product name, navigation, pages, assets and authentication branding are MyShop.

## Run in Visual Studio Insiders

1. Open `eShop.Web.sln`.
2. Set `eShop.Web` as the startup project.
3. Select the `eShop.Web` launch profile and Debug / Any CPU.
4. Use **Build → Rebuild Solution** once after this update, then press F5. The profile opens `https://localhost:5001` (HTTP is `http://localhost:5000`).
5. Keep the SQL Server `MSSQLSERVER` service running. SSMS does not need to remain open.

The browser UI and server run together in this one host. Referenced libraries build automatically. A stable .NET 10 SDK is selected through `global.json`.

CLI alternative:

```powershell
dotnet restore eShop.Web.sln
dotnet run --project eShop.Web/eShop.Web.csproj
```

## Database

Development uses Windows authentication against the default local SQL Server instance:

```text
Data Source=(local);Initial Catalog=eShop;Integrated Security=True;Encrypt=True;TrustServerCertificate=True
```

The original SQL setup file supplied separately creates `Product`, `[Order]` and `OrderLineItem`, and inserts 20 sample products. This machine already has that database installed. Do not rerun the initial database-creation script.

`SeedData` is an optional catalog utility, not a prerequisite for starting the installed application. It now uses explicit columns and inserts missing product IDs inside a transaction; an already populated catalog is left intact. `ConnectionStrings__Default` can override its local connection.

The optional `docs/sql/MyShop-hardening.sql` migration converts SQL price columns from `float` to `decimal(18,2)` and adds data-integrity constraints/indexes. It is not automatically applied. Back up an existing database, inspect its preflight checks, and apply it as a separate database change with a client that stops on errors. The application supports both the original schema and the hardened schema. The redesign did not alter this machine's `eShop` schema or insert test orders into it.

## Administrator access

Go to `/admin` or `/login`. The embedded demonstration account has been removed. Authentication uses a configurable administrator username and a standard ASP.NET Core password hash, with role authorization, antiforgery-protected POST login/logout, request rate limits and session expiry.

On the current development machine, a random local password was configured in development user secrets. Its access details are in `%LOCALAPPDATA%\MyShop\local-admin.txt`, outside the repository and restricted to the current Windows user.

To configure or rotate a local administrator yourself:

```powershell
dotnet run --project tools/MyShop.AdminSetup
```

The setup utility prompts for a username and password. Its `--generate` option creates random local credentials and writes the access file. Run it only when you intend to configure or rotate local access. It never adds credentials to repository configuration.

Configuration keys are `AdminAuthentication:UserName` and `AdminAuthentication:PasswordHash`, under user-secrets ID `MyShop-Development`. Visual Studio's **Manage User Secrets** opens that development configuration. For deployment, supply equivalent configuration using the hosting environment or a proper secret store; development user secrets are not deployed.

## Behavior and boundaries

- Prices are decimal application values, explicitly rounded to cents using `MidpointRounding.AwayFromZero`.
- Checkout rebuilds an order using catalog prices and permitted customer fields. Browser prices, processing state and administrator fields are not trusted.
- The browser stores only product IDs, quantities, a revision and a protected checkout token. The previous cart format is migrated and customer/address data is removed from storage.
- A cart supports up to 100 different products and 99 units of each. Zero quantity removes a line.
- A signed, basket-bound identity makes repeated checkout requests idempotent. Database creation is transactional; processing is an atomic outstanding-to-processed transition.
- Browser Web Locks coordinate cart operations between tabs where supported. Older browsers have synchronous operations and per-circuit serialization, but no cross-tab locking guarantee. Separate tabs refresh their displayed state on interaction/reload.
- Confirmation links are guest bearer links: anyone who has a link can see that order's shipping details. Keep them private. There is no customer account or link-expiry system.
- Product names/images are loaded from the current catalog; sale prices come from the saved order lines. Product images use the existing external catalog URLs with a local fallback.
- Orders are recorded without a payment gateway, inventory reservation, delivery-rate calculation, tax engine or email integration.
- Stored order/processing timestamps are UTC. Date-only presentation does not currently convert to a shopper-selected time zone.

## Checks

The regression project is a dependency-free executable; it is not a `dotnet test` adapter project:

```powershell
dotnet build eShop.Web.sln --configuration Release
dotnet run --project tests/MyShop.RegressionTests --configuration Release
node tests/MyShop.RegressionTests/cart-storage.test.mjs
```

Opt-in real SQL checks require a disposable, original-schema database whose name starts with `MyShopTest_`. The runner refuses `eShop` and other names before opening a connection. It writes fixture orders and test constraints, so use a dedicated fixture and remove it afterward:

```powershell
$env:ConnectionStrings__MyShopTest = 'Data Source=(local);Initial Catalog=MyShopTest_YOUR_UNIQUE_NAME;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tests/MyShop.RegressionTests --configuration Release -- --sql-integration
```

## Deployment

Publish the host:

```powershell
dotnet publish eShop.Web/eShop.Web.csproj --configuration Release --output artifacts/publish
```

Production requires the ASP.NET Core 10 runtime, HTTPS/SignalR support, a supplied SQL connection string, and supplied administrator configuration. Use validated SQL certificates (`Encrypt=True;TrustServerCertificate=False`). Persist the ASP.NET Core Data Protection key ring; multiple instances need the same application name (`MyShop`) and shared keys so cookies and checkout tokens remain valid. Configure trusted reverse-proxy forwarding as appropriate to the deployment before relying on IP-based rate limits or HTTPS detection.

The app retains synchronous repository calls and unpaged lists, and the single configured administrator is not a full user-management system. These should be revisited before a larger deployment.

See `docs/MYSHOP-CHANGES.md` for the change-by-change explanation and validation results. `docs/NET10-MIGRATION.md` and `docs/CODEBASE-REVIEW.md` are historical records from before this redesign and hardening work.
