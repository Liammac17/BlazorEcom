# MyShop redesign and application fixes

The application is now branded **MyShop**, with a beauty-store presentation suited to the existing cosmetics catalog. The Clean Architecture projects and inward domain/application dependencies are retained. Internal eShop namespaces, assembly file names and the installed database name remain compatible with the current Visual Studio solution and SQL setup.

## What changed and why

The visible redesign covers the storefront, product details, bag, checkout, confirmation, administrator sign-in, order lists and processing screen. White, royal blue and neutral gray follow the supplied reference. Typography, spacing, product imagery, keyboard focus, responsive breakpoints, empty states and errors now follow one system.

The functional work addresses the confirmed review findings: untrusted cart prices/status, partial orders, repeat submissions, lost cart updates, malformed storage, missing records, historical price display, asynchronous event contracts, validation/ownership logic, listener cleanup and demonstration authentication.

The following manifest covers **every maintained file added, changed or deleted** in this task. Generated bin/obj cache changes caused by IDE evaluation are restored, rather than included as implementation changes. Screenshots and build output are verification artifacts, not maintained source.

| File | Change and justification |
| --- | --- |
| `docs/MYSHOP-CHANGES.md` | Records each maintained file change, its reason, test evidence, local setup changes and remaining limitations. |
| `docs/sql/MyShop-hardening.sql` | Adds an optional preflight-checked, transactional and rerunnable SQL hardening migration with decimal columns, relationships, checks and indexes, including the required SET options for filtered indexes. Not applied to the user's database. |
| `eShop.CoreBusiness/eShop.CoreBusiness.csproj` | Removes the unused Newtonsoft.Json dependency from the domain layer. |
| `eShop.CoreBusiness/Models/Order.cs` | Enforces positive IDs/prices, a maximum of 99 units per product and 100 distinct lines, prevents overflow, and consistently removes matching lines. Aggregate methods now preserve basic invariants. |
| `eShop.CoreBusiness/Models/OrderLineItem.cs` | Uses decimal for saved sale prices instead of binary floating point. |
| `eShop.CoreBusiness/Models/Product.cs` | Uses decimal for catalog prices, making application arithmetic exact to decimal currency values. |
| `eShop.CoreBusiness/Services/OrderService.cs` | Fixes reversed ownership validation, handles nulls, rejects duplicates and supplied fulfillment metadata, and validates quantity/price/customer-field limits against the installed schema. |
| `eShop.DataStore.SQL.Dapper/Helper/DataAccess.cs` | Adds shared-connection transactions, optional single-row lookup and affected-row counts; validates a missing connection string. Failed operations dispose/roll back their transaction. |
| `eShop.DataStore.SQL.Dapper/Helper/IDataAccess.cs` | Defines transaction and absence-result operations so repository implementations can implement atomic writes and controlled not-found results. |
| `eShop.DataStore.SQL.Dapper/OrderRepository.cs` | Creates headers and lines in one transaction; serializable locking deduplicates retrying checkout GUIDs; excludes client fulfillment fields; atomically processes only outstanding orders; preserves line ownership during updates; batches product hydration with deleted-product fallback; returns null for missing records and orders lists deterministically. |
| `eShop.DataStore.SQL.Dapper/ProductRepository.cs` | Returns null for missing/invalid product IDs; sorts the catalog; searches brand/name and escapes wildcard input so a literal search cannot unexpectedly broaden. |
| `eShop.ShoppingCart.LocalStorage/eShop.ShoppingCart.LocalStorage.csproj` | Removes Newtonsoft.Json because the JS module now owns minimal browser-document persistence. |
| `eShop.ShoppingCart.LocalStorage/ShoppingCart.cs` | Serializes operations per circuit; hydrates authoritative catalog prices; verifies Data Protection checkout tokens against product/quantity fingerprints; removes unavailable products; conditionally clears only the completed basket and disposes its JS module. |
| `eShop.ShoppingCart.LocalStorage/wwwroot/myShopCart.js` | Adds the versioned MyShop cart document, legacy migration, damaged-data recovery, duplicate normalization, size/quantity limits, PII stripping, revision/token invalidation and Web Locks for coordination across supported browser tabs. |
| `eShop.StateStore.LocalStorage/ShoppingCartStateStore.cs` | Counts total quantities for the shopping-bag badge, rather than just distinct lines. |
| `eShop.StateStore.LocalStorage/StateStoreBase.cs` | Isolates and traces individual subscriber failures, allowing other listeners and a committed checkout to complete successfully. |
| `eShop.UseCases/AdminPortal/ProcessOrderScreen/ProcessOrderUseCase.cs` | Validates inputs and uses the atomic repository transition, preventing reprocessing from overwriting audit data. |
| `eShop.UseCases/CustomerPortal/ShoppingCartScreen/PlaceOrderUseCase.cs` | Constructs an allow-listed server order, validates the signed basket identity, compares displayed lines/prices with current catalog data, rejects stale-price submissions, rounds consistently, sets UTC placement time and reuses one retry identity. |
| `eShop.UseCases/CustomerPortal/ViewProductScreen/AddProductToCartUseCase.cs` | Returns an awaitable Task and handles an unavailable product as an observable error; navigation can wait for persisted cart work. |
| `eShop.UseCases/CustomerPortal/ViewProductScreen/Interfaces/IAddProductToCartUseCase.cs` | Changes the add-to-cart contract from void to Task so all callers can await it. |
| `eShop.UseCases/eShop.UseCases.csproj` | Removes the duplicate host's content/publish entries, restoring the application layer's separation from presentation deployment. |
| `eShop.UseCases/PluginInterfaces/DataStore/IOrderRepository.cs` | Adds the conditional TryProcessOrder port rather than using a general full-order update for processing. |
| `eShop.UseCases/PluginInterfaces/UI/_Host.cshtml` | Deleted the unused duplicate Razor host document from the application layer. The actual presentation host remains in eShop.Web. |
| `eShop.UseCases/PluginInterfaces/UI/IShoppingCart.cs` | Removes the unused unimplemented PlaceOrderAsync API and adds checkout-aware conditional clearing to protect a newer basket. |
| `eShop.Web.AdminPortal/_Imports.razor` | Imports the framework/logging/task namespaces required by the rewritten components. |
| `eShop.Web.AdminPortal/Controls/OrderComponent.razor` | Renders branded accessible order rows, destination/status information and links to both outstanding and processed order details. |
| `eShop.Web.AdminPortal/Controls/OrderListComponent.razor` | Adds the styled semantic table, stable row keys, responsive overflow container and informative empty states. |
| `eShop.Web.AdminPortal/eShop.Web.AdminPortal.csproj` | Removes the direct dependency on the hardcoded datastore; admin UI consumes application/domain contracts. |
| `eShop.Web.AdminPortal/Pages/OutstandingOrdersComponent.razor` | Adds the MyShop order-desk heading, status count, tabs, constrained page layout, logged failures and retry behavior; requires the Admin policy. |
| `eShop.Web.AdminPortal/Pages/ProcessedOrdersComponent.razor` | Adds consistent branded layout, processed count/tabs, error/retry behavior and the Admin policy. |
| `eShop.Web.AdminPortal/Pages/ProcessOrderComponent.razor` | Adds a two-column detail/address layout, saved sale prices and totals, missing-order handling, processing audit display, a submission guard and useful error feedback. |
| `eShop.Web.Common/_Imports.razor` | Adds task/framework imports used by the revised shared controls. |
| `eShop.Web.Common/Controls/LoginComponent.razor` | Replaces the query-string credential form with an accessible sign-in prompt that fully navigates to the protected ordinary POST login page. |
| `eShop.Web.Common/Controls/MessageComponent.razor` | Uses consistent accessible notice variants and a real labeled dismissal button. |
| `eShop.Web.Common/Controls/SearchBarComponent.razor` | Updates text on input, handles Enter through form submission, and awaits callbacks. Search now uses the current text rather than the previous change-event value. |
| `eShop.Web.CustomerPortal/_Imports.razor` | Imports logging extensions used by guarded component operations. |
| `eShop.Web.CustomerPortal/Controls/CartComponent.razor` | Adds the bag/unit-count presentation, observes asynchronous refresh failures, dispatches rendering safely and unsubscribes on disposal. |
| `eShop.Web.CustomerPortal/Controls/CustomerFormComponent.razor` | Rebuilds checkout fields with labels, autocomplete and schema-aligned lengths; awaits submission; prevents repeat submission and displays progress/validation feedback. |
| `eShop.Web.CustomerPortal/Controls/LineItemComponent.razor` | Adds product imagery, saved unit price, line total, bounded quantity entry, awaited remove/update handlers, busy states and recoverable errors. |
| `eShop.Web.CustomerPortal/Controls/OrderSummaryComponent.razor` | Uses decimal saved-price totals and total units, with a clear checkout action. Removes hardcoded zero tax/shipping rows rather than presenting calculations that do not exist. |
| `eShop.Web.CustomerPortal/Controls/ProductCardComponent.razor` | Adds responsive merchandise cards, useful image alt text, lazy loading, image fallback, consistent pricing and keyboard-accessible product links. |
| `eShop.Web.CustomerPortal/eShop.Web.CustomerPortal.csproj` | Removes AutoMapper after replacing the five-field mapping with explicit assignments, eliminating configuration/license overhead. Removes the UI's concrete state-store reference. |
| `eShop.Web.CustomerPortal/Pages/OrderConfirmationComponent.razor` | Adds a branded success state, order reference, shipping panel, saved sale prices/totals and a controlled invalid/missing confirmation state. |
| `eShop.Web.CustomerPortal/Pages/PlaceOrderComponent.razor` | Adds the checkout layout, an awaited guarded submit handler, explicit customer-field assignments, logged recoverable errors and a request to review changed basket prices/details. |
| `eShop.Web.CustomerPortal/Pages/SearchProductComponent.razor` | Adds a product-focused hero, search beside the product heading, responsive grid, result count and recovery states. Removes the every-sixth-product skip, keeps hero imagery stable while searching, and resets the textbox when clearing a failed search. |
| `eShop.Web.CustomerPortal/Pages/ShoppingCartComponent.razor` | Adds responsive bag/summary layout, loading/empty/retry states and listener disposal, preventing abandoned page listeners from accumulating. |
| `eShop.Web.CustomerPortal/Pages/ViewProductComponent.razor` | Adds the product-detail layout, missing-product/error state, imagery fallback and an awaited add-to-bag operation with progress feedback. |
| `eShop.Web.CustomerPortal/ViewModels/CustomerViewModel.cs` | Adds database-aligned maximum lengths, display names and customer-friendly required messages so technical property names do not appear in validation. |
| `eShop.Web.sln` | Adds the regression executable and administrator setup tool under solution folders; the existing 11 application projects and startup host remain. |
| `eShop.Web/App.razor` | Adds the default MyShop page title and a branded, navigable not-found state. |
| `eShop.Web/Controllers/AuthenticateController.cs` | Deleted the demonstration controller, removing hardcoded credentials, passwords in URLs and GET-based authentication/logout. |
| `eShop.Web/eShop.Web.csproj` | Adds local development UserSecretsId and MyShop product/assembly-title metadata without renaming existing assembly identifiers. |
| `eShop.Web/Pages/_Host.cshtml` | Adds MyShop metadata, favicon, a HeadOutlet for per-route titles and an accessible error-dismiss button. |
| `eShop.Web/Pages/Login.cshtml` | Adds the styled standalone administrator login form with standard antiforgery POST, labels, autocomplete and noindex metadata. |
| `eShop.Web/Pages/Login.cshtml.cs` | Validates configured hashed credentials, issues the Administrator role, credential-version and eight-hour expiry claims, and permits only safe local redirects. |
| `eShop.Web/Pages/Logout.cshtml` | Adds a styled sign-out confirmation so GET requests cannot silently sign a user out. |
| `eShop.Web/Pages/Logout.cshtml.cs` | Performs sign-out only through antiforgery-protected POST. |
| `eShop.Web/Security/AdminAuthenticationOptions.cs` | Defines username and password-hash settings without committing a default credential. |
| `eShop.Web/Security/AdminAuthenticationService.cs` | Uses the standard ASP.NET Core password hasher, fails closed on bad/missing configuration and validates role, username, credential version and session expiry. |
| `eShop.Web/Security/AdminRevalidatingAuthenticationStateProvider.cs` | Revalidates open Blazor circuits every minute so expired or rotated credentials revoke the active admin UI as well as subsequent HTTP requests. |
| `eShop.Web/Shared/MainLayout.razor` | Adds the centered header, skip link, main landmark and compact footer links. |
| `eShop.Web/Shared/NavMenu.razor` | Updates the dormant template's visible brand label to MyShop for consistency. |
| `eShop.Web/Shared/TopNavBar.razor` | Adds the MyShop wordmark, collection/order navigation, bag icon/count and full-page login/logout navigation appropriate for Razor Pages. |
| `eShop.Web/Startup.cs` | Registers scoped cart state, the missing weather service, stable MyShop Data Protection identity, role/cookie/session policies, one-minute circuit revalidation, login rate limiting and Razor Page endpoints. |
| `eShop.Web/wwwroot/css/site.css` | Replaces template styling with reusable white/blue/gray design tokens, serif branding, responsive layouts, cards, forms/admin tables, visible focus, adequate muted-text contrast and reduced-motion behavior. |
| `eShop.Web/wwwroot/images/product-placeholder.svg` | Adds a local branded image fallback for unavailable external product photos. |
| `eShop.Web/wwwroot/myshop.svg` | Adds a code-native shopping-bag favicon matching the MyShop palette. |
| `Plugins/eShop.DataStore.HardCoded/OrderRepository.cs` | Synchronizes the alternative in-memory adapter, returns snapshots, implements line lookup, uses one checkout identity, and conditionally processes orders. This makes its behavior consistent with the SQL port and preserves the first processing audit. |
| `Plugins/eShop.DataStore.HardCoded/ProductRepository.cs` | Changes sample price literals to decimal so the alternative catalog follows the monetary model. |
| `README.md` | Replaces the old migration-focused entry point with current MyShop setup, Visual Studio run instructions, administrator setup, test commands, deployment requirements, and explicit feature limits. |
| `SeedData/Program.cs` | Uses configured connections, explicit insert columns, decimal sample prices, corrected sample text, one transaction and insert-if-missing IDs. Rerunning seed is now safe for already present products; the utility was not executed during this task. |
| `tests/MyShop.RegressionTests/AuthRegression.cs` | Covers valid/invalid/malformed credentials, removal of the old default, credential rotation and valid/expired/anonymous sessions. |
| `tests/MyShop.RegressionTests/cart-storage.test.mjs` | Executes the actual JS storage module against malformed/legacy data, PII stripping, limits, token rotation, conditional clearing, Web Locks and fallback scenarios. |
| `tests/MyShop.RegressionTests/MyShop.RegressionTests.csproj` | Adds a maintained dependency-free .NET 10 regression executable. |
| `tests/MyShop.RegressionTests/PersistenceTests.cs` | Covers transaction boundaries, retry identity, absence outcomes, wildcard escaping, historical product fallback, ownership rollback and in-memory processing/snapshot behavior. |
| `tests/MyShop.RegressionTests/Program.cs` | Runs and counts domain/cart/checkout/auth tests, including tampering, corruption, rounding, quantity limits, signed token replay, stale-price consent, retries and subscriber isolation. |
| `tests/MyShop.RegressionTests/SqlIntegrationTests.cs` | Adds opt-in actual SQL checks that reject every database except an explicit MyShopTest_ fixture before connecting. Covers real rollback, decimal mapping and concurrent creation/processing. |
| `tools/MyShop.AdminSetup/MyShop.AdminSetup.csproj` | Adds a package-free .NET 10 local administrator provisioning utility. |
| `tools/MyShop.AdminSetup/Program.cs` | Provides masked password entry or explicit random generation, validates the target host and writes configuration via secret-manager JSON stdin. Keeps plaintext access details outside the repo with a current-user-only ACL. |

## Verification

- .NET SDK 10.0.404: final Release solution build passed with zero warnings and errors across the 11 application projects plus the regression/tool projects.
- Release publish passed; the published host was launched and inspected in the browser.
- The final default regression run passed **75 counted assertions**, without accessing SQL.
- The actual JavaScript cart-storage module tests passed.
- Actual SQL checks passed against a separate original-schema fixture: mapping, atomic rollback, sequential/eight-way concurrent idempotency, distinct concurrent orders, single-winner processing and historical missing-product hydration. The original-schema run exercised 19 additional SQL assertions.
- The optional SQL migration was applied twice successfully to a second fresh fixture, producing decimal(18,2) price columns, two foreign keys, three check constraints and a unique checkout index. Actual SQL checks also passed on this hardened schema (18 additional assertions; orphan insertion is intentionally blocked by the foreign key).
- Browser checks passed for all 20 catalog cards, Enter-key search, no-match/reset behavior, product detail, add-to-bag, quantity updates, unit counts, decimal totals, persistence across reload, and empty checkout validation.
- A full browser checkout using fictional data in the isolated fixture reached confirmation, cleared the bag, appeared in administration, was processed, appeared in processed orders and completed POST sign-out.
- Administrator login issued the expected role cookie; a POST without an antiforgery token returned HTTP 400; the former query-string login issued no authentication cookie. The final published login and empty order desk were checked again after session revalidation was added.
- Desktop, tablet and phone breakpoints were inspected; measured document width did not exceed the viewport width. Screenshots are saved under `artifacts/myshop/`.
- The normal local `eShop` database retained its 20 products, zero orders and zero line items. No schema migration or seed operation was run against it. Temporary fixture databases and owned test hosts are removed/stopped during final teardown.

## Local setup changes

A random local administrator was configured through the existing host's new development user-secrets ID `MyShop-Development`. Only the username and password hash are used by the app. The access details are in `%LOCALAPPDATA%/MyShop/local-admin.txt`, restricted to the current Windows user, outside the repository. These are development settings and are not deployed.

Run `dotnet run --project tools/MyShop.AdminSetup` to choose a new local password, or pass `-- --generate` to deliberately generate replacements. Existing demonstration credentials no longer authenticate.

Open `eShop.Web.sln` in Visual Studio Insiders, keep `eShop.Web` as the startup project and select its `eShop.Web` profile. F5 launches the server and UI together at `https://localhost:5001`.

## Explicit limits

This work does not add payment processing, inventory reservations, real tax/delivery calculation, email, customer accounts, order-link expiry or a full administrator user store. Guest confirmation is still a private bearer link. Displayed product names/images remain current catalog data; sale prices are snapshots. Timestamps are stored in UTC.

Repository methods remain synchronous, and lists are unpaged. Web Locks supply cross-tab mutation coordination only in supporting browsers; other browsers retain the stated synchronous/per-circuit fallback. Separate tabs refresh their displayed basket on interaction/reload.

The existing SQL schema still uses float prices and lacks the optional constraints/indexes until the supplied migration is separately reviewed, backed up and applied. Decimal application mapping works with both schemas and was tested on each.

Production still needs hosting-specific HTTPS, SQL credentials/certificate configuration, administrator secrets and a persisted/shared Data Protection key ring for multiple instances. Rate-limit proxy/IP handling must match the actual deployment. Passing these tests is not a blanket production-readiness claim.

Historical migration/review records remain in `docs/NET10-MIGRATION.md` and `docs/CODEBASE-REVIEW.md`; the README points to this document for current behavior.


## Copy cleanup

Removed promotional slogans, announcement text, decorative captions and repeated descriptions. Shortened navigation, headings, forms and errors. Functionality is unchanged. Release build passed with zero warnings or errors.

## Reference styling update

- Switched the shared stylesheet to white, royal blue and neutral gray, with pill buttons and inputs, fine borders, and matching customer/admin/auth layouts.
- Centered the MyShop serif wordmark and simplified the header navigation.
- Replaced the old hero with a blue diamond-pattern banner and the existing working product search inside a white panel.
- Rebuilt the footer with useful links and an oversized blue wordmark. No newsletter or inactive links were added.
- Added `wwwroot/images/blue-pattern.svg`; recolored the favicon and product-image fallback to match.
- Kept the existing short copy and shopping behavior. Release build passed without warnings or errors; desktop and phone layouts were checked in the browser.

## Storefront opening and footer revision

- Removed the large footer wordmark and its styling to keep the footer compact.
- Replaced the patterned search banner with a short product hero, actual catalog images, and a Shop products link.
- Moved search beside the Products heading; on smaller screens it sits directly above the grid.
- Featured products remain unchanged while filtering the catalog. Existing search and reset behavior are retained.
- Release build passed with zero warnings and errors.
