using System.Data;
using System.Text.RegularExpressions;
using eShop.CoreBusiness.Models;
using eShop.DataStore.SQL.Dapper;
using Microsoft.Data.SqlClient;
using SqlOrderRepository = eShop.DataStore.SQL.Dapper.OrderRepository;
using SqlProductRepository = eShop.DataStore.SQL.Dapper.ProductRepository;

// Opt-in integration checks. The caller creates and drops an isolated fixture database.
// Never use the application's normal connection string or database for these checks.
internal static class SqlIntegrationTests
{
    public static async Task RunAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MyShopTest");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("--sql-integration requires ConnectionStrings__MyShopTest for an isolated fixture database.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!Regex.IsMatch(builder.InitialCatalog ?? "", "^MyShopTest_[A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("SQL integration refuses every database except an explicitly named MyShopTest_ fixture. eShop is never permitted.");
        if (!string.IsNullOrWhiteSpace(builder.AttachDBFilename))
            throw new InvalidOperationException("AttachDBFilename is not permitted for the fixture checks.");

        var access = new DataAccess(builder.ConnectionString);
        var actualDatabase = access.QuerySingle<string, object>("SELECT DB_NAME()", new { });
        if (!string.Equals(actualDatabase, builder.InitialCatalog, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The connected database does not match the fixture name.");
        Regression.Check(actualDatabase.StartsWith("MyShopTest_", StringComparison.OrdinalIgnoreCase),
            "SQL integration is restricted to the isolated MyShopTest_ database");
        Console.WriteLine("SQL fixture: " + actualDatabase);

        var products = new SqlProductRepository(access);
        var repository = new SqlOrderRepository(access);
        var product = products.GetProducts().FirstOrDefault()
            ?? throw new InvalidOperationException("The fixture must contain at least one sample product.");
        Regression.Check(product.Price > 0m && product.Price == decimal.Round(product.Price, 2),
            "The real SQL provider maps the original catalog price column into decimal values");
        Regression.Check(products.GetProduct(int.MaxValue) == null && repository.GetOrder(int.MaxValue) == null,
            "Real SQL lookups return absence for unknown product and order IDs");
        Regression.Check(repository.GetOrderByUniqueId(Guid.NewGuid().ToString()) == null,
            "Unknown confirmation GUIDs return absence against SQL Server");

        var order = NewOrder(product.ProductId);
        order.DateProcessed = DateTime.UtcNow;
        order.DateProcessing = DateTime.UtcNow;
        order.AdminUser = "forged-admin";
        var id = repository.CreateOrder(order);
        var saved = repository.GetOrder(id);
        Regression.Check(saved != null && saved.LineItems.Count == 1 && saved.LineItems[0].Product.ProductId == product.ProductId,
            "The real SQL repository persists and hydrates an order and its joined product");
        Regression.Check(saved.LineItems[0].Price == 10.10m && saved.LineItems[0].Quantity == 2,
            "Stored order prices and quantities round-trip as exact decimal application values");
        Regression.Check(saved.DateProcessed == null && saved.DateProcessing == null && saved.AdminUser == null,
            "SQL order creation independently excludes client fulfillment metadata");
        var retryId = repository.CreateOrder(NewOrder(product.ProductId, order.UniqueId));
        Regression.Check(retryId == id && CountOrders(access, order.UniqueId) == 1 && CountLines(access, id) == 1,
            "A sequential retry produces one SQL header and one set of lines");

        var concurrentToken = Guid.NewGuid().ToString();
        var concurrentResults = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            new SqlOrderRepository(new DataAccess(builder.ConnectionString)).CreateOrder(NewOrder(product.ProductId, concurrentToken)))));
        Regression.Check(concurrentResults.Distinct().Count() == 1 && CountOrders(access, concurrentToken) == 1
            && CountLines(access, concurrentResults[0]) == 1,
            "Eight concurrent requests with the same checkout token create exactly one SQL order");

        var distinctTokens = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var distinctResults = await Task.WhenAll(distinctTokens.Select(token => Task.Run(() =>
            new SqlOrderRepository(new DataAccess(builder.ConnectionString)).CreateOrder(NewOrder(product.ProductId, token)))));
        Regression.Check(distinctResults.Distinct().Count() == distinctTokens.Length
            && distinctTokens.All(token => CountOrders(access, token) == 1),
            "Concurrent distinct checkouts each create one complete SQL order");

        var processingTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var processResults = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            (Admin: "test-admin-" + index, Won: new SqlOrderRepository(new DataAccess(builder.ConnectionString))
                .TryProcessOrder(id, "test-admin-" + index, processingTime.AddSeconds(index))))));
        var winner = processResults.Single(result => result.Won);
        var afterProcessing = repository.GetOrder(id);
        Regression.Check(processResults.Count(result => result.Won) == 1 && afterProcessing.AdminUser == winner.Admin,
            "Concurrent processing has a single SQL winner with the matching administrator audit");
        var originalTimestamp = afterProcessing.DateProcessed;
        Regression.Check(!repository.TryProcessOrder(id, "later-admin", processingTime.AddHours(1))
            && repository.GetOrder(id).DateProcessed == originalTimestamp
            && repository.GetOrder(id).AdminUser == winner.Admin,
            "A later process request cannot overwrite the first SQL processing audit");

        var rollbackToken = Guid.NewGuid().ToString();
        var rollbackOrder = NewOrder(product.ProductId, rollbackToken);
        rollbackOrder.LineItems.Add(new OrderLineItem { ProductId = product.ProductId, Quantity = 0, Price = 4m });
        var beforeLineCount = access.QuerySingle<int, object>("SELECT COUNT(*) FROM dbo.OrderLineItem", new { });
        var constraintName = "CK_MyShopTest_Quantity_" + Guid.NewGuid().ToString("N");
        access.ExecuteCommand($"ALTER TABLE dbo.OrderLineItem WITH CHECK ADD CONSTRAINT [{constraintName}] CHECK (Quantity > 0)", new { });
        try
        {
            Regression.Throws<SqlException>(() => repository.CreateOrder(rollbackOrder),
                "A real SQL constraint failure on the second line interrupts checkout");
            Regression.Check(CountOrders(access, rollbackToken) == 0
                && access.QuerySingle<int, object>("SELECT COUNT(*) FROM dbo.OrderLineItem", new { }) == beforeLineCount,
                "SQL Server rolls back both the header and the earlier successful line after the later line fails");
            Regression.Check(!rollbackOrder.OrderId.HasValue && rollbackOrder.LineItems.All(line => !line.OrderId.HasValue),
                "Failed SQL creation leaves the caller's order unpersisted");
        }
        finally
        {
            access.ExecuteCommand($"ALTER TABLE dbo.OrderLineItem DROP CONSTRAINT [{constraintName}]", new { });
        }

        var updateBefore = repository.GetOrder(id);
        var invalidUpdate = repository.GetOrder(id);
        invalidUpdate.CustomerName = "This must roll back";
        invalidUpdate.LineItems[0].OrderId = int.MaxValue;
        Regression.Throws<ArgumentException>(() => repository.UpdateOrder(invalidUpdate),
            "A real SQL update rejects foreign line ownership after its header operation");
        Regression.Check(repository.GetOrder(id).CustomerName == updateBefore.CustomerName,
            "SQL Server rolls back the header change from an invalid order update");

        var hasProductForeignKey = access.QuerySingle<int, object>(@"
            SELECT COUNT(*) FROM sys.foreign_key_columns
            WHERE parent_object_id = OBJECT_ID(N'dbo.OrderLineItem')
              AND referenced_object_id = OBJECT_ID(N'dbo.Product')", new { }) > 0;
        if (!hasProductForeignKey)
        {
            var historicalOrder = NewOrder(int.MaxValue);
            var historicalId = repository.CreateOrder(historicalOrder);
            var historical = repository.GetOrder(historicalId).LineItems.Single();
            Regression.Check(historical.Product.Name.Contains("Unavailable") && historical.Price == 10.10m,
                "The original SQL schema can hydrate historical lines whose catalog product is absent");
        }
        else
        {
            Console.WriteLine("INFO: Product foreign key prevents new orphan fixtures; deleted-product fallback was already covered without SQL.");
        }
        Regression.Check(repository.GetProcessedOrders().Any(item => item.OrderId == id)
            && repository.GetOutstandingOrders().Any(item => item.OrderId == concurrentResults[0]),
            "Processed and outstanding lists reflect real SQL state transitions");
    }

    private static Order NewOrder(int productId, string token = null) => new()
    {
        UniqueId = token ?? Guid.NewGuid().ToString(),
        DatePlaced = DateTime.UtcNow,
        CustomerName = "MyShop SQL fixture",
        CustomerAddress = "1 Integration Street",
        CustomerCity = "Denver",
        CustomerStateProvince = "Colorado",
        CustomerCountry = "United States",
        LineItems = new List<OrderLineItem>
        {
            new() { ProductId = productId, Price = 10.10m, Quantity = 2 }
        }
    };

    private static int CountOrders(IDataAccess access, string token)
        => access.QuerySingle<int, object>("SELECT COUNT(*) FROM dbo.[Order] WHERE UniqueId = @UniqueId", new { UniqueId = token });
    private static int CountLines(IDataAccess access, int id)
        => access.QuerySingle<int, object>("SELECT COUNT(*) FROM dbo.OrderLineItem WHERE OrderId = @OrderId", new { OrderId = id });
}
