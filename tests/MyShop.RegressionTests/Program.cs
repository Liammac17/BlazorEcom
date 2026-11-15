using eShop.CoreBusiness.Models;
using eShop.CoreBusiness.Services;
using eShop.ShoppingCart.LocalStorage;
using eShop.StateStore.LocalStorage;
using eShop.UseCases.PluginInterfaces.DataStore;
using eShop.UseCases.PluginInterfaces.UI;
using eShop.UseCases.ProcessOrderScreen;
using eShop.UseCases.ShoppingCartScreen;
using eShop.UseCases.ViewProductScreen;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using System.Text.Json;
using MemoryOrderRepository = eShop.DataStore.HardCoded.OrderRepository;
using CartDocument = eShop.ShoppingCart.LocalStorage.ShoppingCart.CartDocument;
using CartItem = eShop.ShoppingCart.LocalStorage.ShoppingCart.CartItem;

await DomainAndCartTests.RunAsync();
await PersistenceTests.RunAsync();
AuthRegression.Run();
var sqlIntegration = args.Contains("--sql-integration", StringComparer.OrdinalIgnoreCase);
if (sqlIntegration) await SqlIntegrationTests.RunAsync();
Console.WriteLine($"PASS: {Regression.Assertions} regression assertions completed. "
    + (sqlIntegration ? "SQL integration checks completed against the explicitly configured test database."
        : "No SQL database was accessed."));

internal static class Regression
{
    public static int Assertions { get; private set; }
    public static void Check(bool condition, string description)
    {
        Assertions++;
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
    public static void Throws<T>(Action action, string description) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, description); return; }
        throw new InvalidOperationException("FAIL: " + description);
    }
    public static async Task ThrowsAsync<T>(Func<Task> action, string description) where T : Exception
    {
        try { await action(); }
        catch (T) { Check(true, description); return; }
        throw new InvalidOperationException("FAIL: " + description);
    }
}

internal static class DomainAndCartTests
{
    public static async Task RunAsync()
    {
        var validation = new OrderService();
        var order = ValidOrder();
        Regression.Check(validation.ValidateCreateOrder(order), "A valid new order passes business validation");
        order.DateProcessed = DateTime.UtcNow;
        Regression.Check(!validation.ValidateCreateOrder(order), "New orders cannot arrive already processed");
        order.DateProcessed = null;
        order.CustomerCity = new string('x', 51);
        Regression.Check(!validation.ValidateCreateOrder(order), "Customer field lengths respect the existing SQL schema");
        order.CustomerCity = "Denver";
        order.LineItems.Add(new OrderLineItem { ProductId = 1, Quantity = 1, Price = 10m });
        Regression.Check(!validation.ValidateCreateOrder(order), "Duplicate product lines are rejected");
        order.LineItems.RemoveAt(1);
        order.OrderId = 42;
        order.LineItems[0].OrderId = 42;
        Regression.Check(validation.ValidateUpdateOrder(order), "A line belonging to its order passes ownership validation");
        order.LineItems[0].OrderId = 77;
        Regression.Check(!validation.ValidateUpdateOrder(order), "A line belonging to another order fails ownership validation");
        Regression.Check(!validation.ValidateProcessOrder(null), "Missing orders fail processing validation safely");

        var bounded = new Order();
        bounded.AddProduct(1, 99, 0.10m);
        Regression.Throws<ArgumentOutOfRangeException>(() => bounded.AddProduct(1, 1, 0.10m), "Quantity overflow is rejected before the aggregate changes");
        Regression.Check(bounded.LineItems[0].Quantity == 99, "Rejected quantity changes preserve the previous valid quantity");
        Regression.Check(0.10m * 3 == 0.30m, "Monetary calculations use exact decimal arithmetic");

        var catalog = new Catalog();
        var storage = new BrowserStorage();
        var protector = new EphemeralDataProtectionProvider();
        await using var cart = new ShoppingCart(storage, catalog, protector);
        var initial = await cart.GetOrderAsync();
        var reloaded = await cart.GetOrderAsync();
        Regression.Check(Guid.TryParse(initial.UniqueId, out _) && initial.UniqueId == reloaded.UniqueId,
            "An unchanged basket retains its server-issued checkout identity");
        await Task.WhenAll(cart.AddProductAsync(catalog.GetProduct(1)), cart.AddProductAsync(catalog.GetProduct(2)));
        var basket = await cart.GetOrderAsync();
        Regression.Check(basket.LineItems.Count == 2, "Overlapping cart additions preserve both products");
        Regression.Check(basket.UniqueId != initial.UniqueId, "Changing basket contents rotates the checkout identity");
        await cart.UpdateQuantityAsync(1, 3);
        var count = await new ShoppingCartStateStore(cart).GetLineItemsCount();
        Regression.Check(count == 4, "The cart badge counts units rather than distinct product rows");

        var signed = await cart.GetOrderAsync();
        var protectedToken = storage.Module.Document.CheckoutToken;
        storage.Module.Document.CheckoutToken = Guid.NewGuid().ToString();
        var repaired = await cart.GetOrderAsync();
        Regression.Check(repaired.UniqueId != signed.UniqueId, "An unsigned browser checkout identifier is discarded");
        storage.Module.Document.CheckoutToken = protectedToken;
        storage.Module.Document.Items[0].Quantity = 7;
        var changed = await cart.GetOrderAsync();
        Regression.Check(changed.UniqueId != signed.UniqueId, "A protected token cannot be reused with altered product quantities");

        var beforeUnavailable = changed.UniqueId;
        catalog.Products.Remove(2);
        var pruned = await cart.GetOrderAsync();
        Regression.Check(pruned.LineItems.Count == 1 && storage.Module.Document.Items.Count == 1,
            "Deleted catalog products are removed from persistent basket contents");
        Regression.Check(pruned.UniqueId != beforeUnavailable, "Removing an unavailable product rotates the checkout identity");

        var repository = new MemoryOrderRepository();
        var state = new ShoppingCartStateStore(cart);
        var healthyListenerCalled = false;
        state.AddStateChangeListeners(() => throw new InvalidOperationException("Simulated stale view."));
        state.AddStateChangeListeners(() => healthyListenerCalled = true);
        var checkout = new PlaceOrderUseCase(cart, repository, validation, state, catalog);
        var proposed = await cart.GetOrderAsync();
        proposed.CustomerName = " Test Customer ";
        proposed.CustomerAddress = " 123 Test Street ";
        proposed.CustomerCity = " Denver ";
        proposed.CustomerStateProvince = " Colorado ";
        proposed.CustomerCountry = " United States ";
        proposed.AdminUser = "injected-admin";
        proposed.DateProcessed = DateTime.UtcNow;
        proposed.LineItems[0].Price = 0.01m;
        Regression.Check(await checkout.Execute(proposed) == null && !repository.GetOrders().Any(),
            "A hostile claimed price is rejected without persisting an order");
        proposed.LineItems[0].Price = 14.99m;
        var submittedLines = proposed.LineItems;
        proposed.LineItems = null;
        Regression.Check(await checkout.Execute(proposed) == null && !repository.GetOrders().Any(),
            "A missing submitted line collection is rejected without writing");
        proposed.LineItems = new List<OrderLineItem> { null };
        Regression.Check(await checkout.Execute(proposed) == null && !repository.GetOrders().Any(),
            "A null submitted line is rejected without writing");
        proposed.LineItems = new List<OrderLineItem> { submittedLines[0], submittedLines[0] };
        Regression.Check(await checkout.Execute(proposed) == null && !repository.GetOrders().Any(),
            "Duplicate submitted lines are rejected without writing");
        proposed.LineItems = submittedLines;
        proposed.LineItems[0].ProductId = 999;
        Regression.Check(await checkout.Execute(proposed) == null && !repository.GetOrders().Any(),
            "A different submitted product identifier is rejected without writing");
        proposed.LineItems[0].ProductId = 1;
        var agreedQuantity = proposed.LineItems[0].Quantity;
        proposed.LineItems[0].Quantity++;
        Regression.Check(await checkout.Execute(proposed) == null && !repository.GetOrders().Any(),
            "A different submitted quantity is rejected without writing");
        proposed.LineItems[0].Quantity = agreedQuantity;

        catalog.Products[1].Price = 19.99m;
        Regression.Check(await checkout.Execute(proposed) == null && !repository.GetOrders().Any(),
            "A catalog price increase after checkout loads requires a fresh review and writes no order");
        proposed.LineItems = (await cart.GetOrderAsync()).LineItems;
        var confirmed = await checkout.Execute(proposed);
        var saved = repository.GetOrderByUniqueId(confirmed);
        Regression.Check(saved.LineItems[0].Price == 19.99m,
            "Refreshing the basket accepts the newly reviewed price from the authoritative product repository");
        Regression.Check(saved.AdminUser == null && saved.DateProcessed == null && saved.DateProcessing == null,
            "Checkout discards browser-supplied fulfillment and administrator metadata");
        Regression.Check(saved.CustomerName == "Test Customer", "Checkout trims and allow-lists the shipping fields");
        Regression.Check((await cart.GetOrderAsync()).LineItems.Count == 0, "Successful checkout clears its own basket");
        Regression.Check(confirmed == saved.UniqueId && healthyListenerCalled,
            "A failed state subscriber cannot fail a committed checkout or suppress the other subscriber");

        catalog.Products[1].Price = 14.99m;
        await cart.AddProductAsync(catalog.GetProduct(1));
        var newBasket = await cart.GetOrderAsync();
        await cart.EmptyAsync(confirmed);
        Regression.Check((await cart.GetOrderAsync()).UniqueId == newBasket.UniqueId,
            "Finishing an older checkout cannot clear a newer basket");
        var forged = ValidOrder();
        forged.UniqueId = confirmed;
        Regression.Check(await checkout.Execute(forged) == null, "A supplied existing order GUID cannot select another checkout");

        var retryCart = new RetryCart(ValidOrder());
        var retryRepository = new MemoryOrderRepository();
        var retryCheckout = new PlaceOrderUseCase(retryCart, retryRepository, validation,
            new ShoppingCartStateStore(retryCart), catalog);
        await Regression.ThrowsAsync<InvalidOperationException>(() => retryCheckout.Execute(retryCart.Order),
            "A simulated browser-clear failure occurs after the order commit");
        var retryId = await retryCheckout.Execute(retryCart.Order);
        Regression.Check(retryRepository.GetOrders().Count() == 1 && retryId == retryCart.Order.UniqueId,
            "Retrying after a committed-order cleanup failure reuses the same order");

        var process = new ProcessOrderUseCase(repository, validation);
        Regression.Check(process.Execute(saved.OrderId.Value, "admin"), "An outstanding order can be marked processed");
        var firstProcessed = repository.GetOrder(saved.OrderId.Value);
        Regression.Check(!process.Execute(saved.OrderId.Value, "other-admin"), "Repeated processing fails instead of replacing audit data");
        Regression.Check(repository.GetOrder(saved.OrderId.Value).AdminUser == firstProcessed.AdminUser,
            "Repeated processing retains the original administrator");

        var missingAdd = new AddProductToCartUseCase(cart, catalog, state);
        await Regression.ThrowsAsync<InvalidOperationException>(() => missingAdd.Execute(999),
            "Add-to-cart missing-product failures can be awaited and handled by the UI");

        catalog.Products[3] = new Product { ProductId = 3, Name = "Rounding boundary", Price = 14.985m };
        await cart.AddProductAsync(catalog.GetProduct(3));
        var roundingProposal = await cart.GetOrderAsync();
        Regression.Check(roundingProposal.LineItems.Single(item => item.ProductId == 3).Price == 14.99m,
            "Basket prices explicitly round midpoint cents away from zero");
        roundingProposal.CustomerName = "Customer";
        roundingProposal.CustomerAddress = "123 Test Street";
        roundingProposal.CustomerCity = "Denver";
        roundingProposal.CustomerStateProvince = "Colorado";
        roundingProposal.CustomerCountry = "United States";
        var roundingRepository = new MemoryOrderRepository();
        var roundingCheckout = new PlaceOrderUseCase(cart, roundingRepository, validation, state, catalog);
        var roundedId = await roundingCheckout.Execute(roundingProposal);
        Regression.Check(roundingRepository.GetOrderByUniqueId(roundedId).LineItems.Single(item => item.ProductId == 3).Price == 14.99m,
            "Checkout uses the same explicit midpoint rounding policy as the basket");
    }

    private static Order ValidOrder() => new Order
    {
        UniqueId = Guid.NewGuid().ToString(),
        DatePlaced = DateTime.UtcNow,
        CustomerName = "Customer", CustomerAddress = "123 Test Street", CustomerCity = "Denver",
        CustomerStateProvince = "Colorado", CustomerCountry = "United States",
        LineItems = new List<OrderLineItem> { new OrderLineItem { ProductId = 1, Quantity = 2, Price = 14.99m } }
    };

    private sealed class Catalog : IProductRepository
    {
        public Dictionary<int, Product> Products { get; } = new Dictionary<int, Product>
        {
            [1] = new Product { ProductId = 1, Name = "Product One", Price = 14.99m },
            [2] = new Product { ProductId = 2, Name = "Product Two", Price = 9.99m }
        };
        public Product GetProduct(int id) => Products.GetValueOrDefault(id);
        public IEnumerable<Product> GetProducts(string filter = null) => Products.Values;
    }

    private sealed class RetryCart : IShoppingCart
    {
        private bool failClear = true;
        public Order Order { get; }
        public RetryCart(Order order) { Order = order; }
        public Task<Order> GetOrderAsync() => Task.FromResult(Order);
        public Task EmptyAsync(string checkoutId = null)
        {
            if (failClear) { failClear = false; throw new InvalidOperationException("Simulated disconnected browser."); }
            return Task.CompletedTask;
        }
        public Task<Order> AddProductAsync(Product product) => throw new NotSupportedException();
        public Task<Order> DeleteProductAsync(int productId) => throw new NotSupportedException();
        public Task<Order> UpdateQuantityAsync(int productId, int quantity) => throw new NotSupportedException();
        public Task UpdateOrderAsync(Order order) => Task.CompletedTask;
    }

    private sealed class BrowserStorage : IJSRuntime
    {
        public StorageModule Module { get; } = new StorageModule();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object[] args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object[] args)
        {
            if (identifier != "import") throw new InvalidOperationException("Only module import is expected.");
            return ValueTask.FromResult((TValue)(object)Module);
        }
    }

    private sealed class StorageModule : IJSObjectReference
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        public CartDocument Document { get; set; } = new CartDocument();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object[] args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object[] args)
        {
            await Task.Delay(1, cancellationToken);
            var operation = (string)args[0];
            var payload = JsonSerializer.SerializeToElement(args.Length > 1 ? args[1] : null, Json);
            var changed = false;
            if (operation == "token")
            {
                var fingerprint = string.Join(",", Document.Items.OrderBy(item => item.ProductId).Select(item => $"{item.ProductId}:{item.Quantity}"));
                if (Document.Revision == payload.GetProperty("revision").GetInt32()
                    && fingerprint == payload.GetProperty("fingerprint").GetString())
                    Document.CheckoutToken = payload.GetProperty("checkoutToken").GetString();
            }
            else if (operation == "add")
            {
                var id = payload.GetProperty("productId").GetInt32();
                var existing = Document.Items.SingleOrDefault(item => item.ProductId == id);
                if (existing == null) Document.Items.Add(new CartItem { ProductId = id, Quantity = 1 });
                else existing.Quantity++;
                changed = true;
            }
            else if (operation == "quantity" || operation == "remove")
            {
                var id = payload.GetProperty("productId").GetInt32();
                var quantity = operation == "remove" ? 0 : payload.GetProperty("quantity").GetInt32();
                if (quantity == 0) Document.Items.RemoveAll(item => item.ProductId == id);
                else Document.Items.Single(item => item.ProductId == id).Quantity = quantity;
                changed = true;
            }
            else if (operation == "prune")
            {
                var ids = payload.GetProperty("productIds").EnumerateArray().Select(item => item.GetInt32()).ToHashSet();
                Document.Items.RemoveAll(item => ids.Contains(item.ProductId));
                changed = true;
            }
            else if (operation == "clear")
            {
                if (Document.CheckoutToken == payload.GetProperty("checkoutToken").GetString())
                { Document.Items.Clear(); changed = true; }
            }
            else if (operation == "replace")
            {
                Document.Items = payload.GetProperty("items").Deserialize<List<CartItem>>(Json);
                changed = true;
            }
            else if (operation != "read") throw new InvalidOperationException("Unexpected module operation: " + operation);
            if (changed) { Document.Revision++; Document.CheckoutToken = null; }
            Document.Items = Document.Items.OrderBy(item => item.ProductId).ToList();
            return (TValue)(object)JsonSerializer.Deserialize<CartDocument>(JsonSerializer.Serialize(Document, Json), Json);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
