using System.Collections;
using System.Data;
using eShop.CoreBusiness.Models;
using eShop.DataStore.SQL.Dapper;
using SqlOrderRepository = eShop.DataStore.SQL.Dapper.OrderRepository;
using SqlProductRepository = eShop.DataStore.SQL.Dapper.ProductRepository;
using MemoryOrderRepository = eShop.DataStore.HardCoded.OrderRepository;

internal static class PersistenceTests
{
    public static Task RunAsync()
    {
        var order = NewOrder();
        var failureStore = new RecordingDataAccess { FailLineInsert = true };
        Regression.Throws<InvalidOperationException>(() => new SqlOrderRepository(failureStore).CreateOrder(order), "Order creation surfaces a failed line insert");
        Regression.Check(failureStore.Headers == 0 && failureStore.Lines == 0 && failureStore.Rollbacks == 1,
            "A failed line insert rolls the header back through the transaction boundary");
        Regression.Check(order.OrderId == null && order.LineItems[0].OrderId == null,
            "Failed creation does not mark the caller's order as persisted");

        var store = new RecordingDataAccess();
        var repository = new SqlOrderRepository(store);
        var id = repository.CreateOrder(NewOrder());
        var retry = NewOrder();
        var sameId = repository.CreateOrder(retry);
        Regression.Check(id == sameId && store.Headers == 1 && store.Lines == 1,
            "Repeated checkout identifiers reuse the existing order without inserting lines again");
        Regression.Check(store.AllWritesTransactional, "Every create write participates in the same transaction");

        var noRecords = new RecordingDataAccess();
        Regression.Check(new SqlProductRepository(noRecords).GetProduct(999) == null,
            "Missing SQL products produce an absence result");
        Regression.Check(new SqlOrderRepository(noRecords).GetOrder(999) == null,
            "Missing SQL orders produce an absence result");
        Regression.Check(new SqlOrderRepository(noRecords).GetOrderByUniqueId("not-a-guid") == null,
            "Invalid confirmation tokens do not become SQL lookups");

        var searchStore = new RecordingDataAccess();
        new SqlProductRepository(searchStore).GetProducts("a%_[~").ToList();
        Regression.Check((string)searchStore.LastParameters.GetType().GetProperty("Filter").GetValue(searchStore.LastParameters) == "%a~%~_~[~~%",
            "Catalog search escapes wildcard characters instead of broadening the search");

        var missingCatalog = new RecordingDataAccess { ReturnMissingCatalogLine = true };
        var historicalLine = new SqlOrderRepository(missingCatalog).GetLineItemsByOrderId(42).Single();
        Regression.Check(historicalLine.Product.Name.Contains("Unavailable") && historicalLine.Price == 10m && historicalLine.Product.Price == 10m,
            "Historical order lines survive a deleted catalog product using their stored price");
        Regression.Check(missingCatalog.Queries == 1, "Order line hydration uses one joined query rather than one lookup per product");

        var updateStore = new RecordingDataAccess();
        var foreignLine = NewOrder();
        foreignLine.OrderId = 42;
        foreignLine.LineItems[0].LineItemId = 1;
        foreignLine.LineItems[0].OrderId = 77;
        Regression.Throws<ArgumentException>(() => new SqlOrderRepository(updateStore).UpdateOrder(foreignLine),
            "Updating an order rejects foreign line ownership");
        Regression.Check(updateStore.HeaderUpdates == 0 && updateStore.Rollbacks == 1,
            "An invalid order update rolls its preceding header write back");

        var memory = new MemoryOrderRepository();
        var original = NewOrder();
        original.DateProcessed = DateTime.UtcNow;
        original.AdminUser = "forged";
        var memoryId = memory.CreateOrder(original);
        Regression.Check(memory.GetOrder(memoryId).DateProcessed == null && memory.GetOrder(memoryId).AdminUser == null,
            "The in-memory adapter also discards client fulfillment metadata on creation");
        Regression.Check(memory.CreateOrder(NewOrder()) == memoryId && memory.GetOrders().Count() == 1,
            "The in-memory adapter implements checkout idempotency");
        var firstTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        Regression.Check(memory.TryProcessOrder(memoryId, "first-admin", firstTime), "Outstanding orders can be processed");
        Regression.Check(!memory.TryProcessOrder(memoryId, "second-admin", firstTime.AddMinutes(5)), "Already processed orders cannot be processed again");
        var processed = memory.GetOrder(memoryId);
        Regression.Check(processed.AdminUser == "first-admin" && processed.DateProcessed == firstTime,
            "Repeated processing preserves the original administrator and timestamp");
        processed.CustomerName = "changed outside repository";
        Regression.Check(memory.GetOrder(memoryId).CustomerName == "Test Customer", "Repository reads do not expose mutable in-memory stored objects");
        Regression.Check(memory.GetLineItemsByOrderId(memoryId).Count() == 1 && !memory.GetLineItemsByOrderId(999).Any(),
            "The in-memory line-item port is implemented for present and missing orders");
        return Task.CompletedTask;
    }

    private static Order NewOrder() => new()
    {
        UniqueId = "f5b254d2-3d61-4d55-ac51-b81d63a1cab5",
        DatePlaced = DateTime.UtcNow,
        CustomerName = "Test Customer",
        CustomerAddress = "1 Main Street",
        CustomerCity = "Denver",
        CustomerStateProvince = "Colorado",
        CustomerCountry = "United States",
        LineItems = new List<OrderLineItem>
        {
            new() { ProductId = 495, Price = 10m, Quantity = 2 }
        }
    };

    private sealed class RecordingDataAccess : IDataAccess
    {
        private int depth;
        private int? persistedId;
        public bool FailLineInsert { get; init; }
        public bool ReturnMissingCatalogLine { get; init; }
        public int Headers { get; private set; }
        public int Lines { get; private set; }
        public int HeaderUpdates { get; private set; }
        public int Rollbacks { get; private set; }
        public int Queries { get; private set; }
        public bool AllWritesTransactional { get; private set; } = true;
        public object LastParameters { get; private set; }

        public int ExecuteCommand<T>(string sql, T parameters)
        {
            LastParameters = parameters;
            AllWritesTransactional &= depth > 0;
            if (sql.Contains("INSERT INTO dbo.OrderLineItem"))
            {
                if (FailLineInsert) throw new InvalidOperationException("Simulated line-insert failure");
                var count = ((IEnumerable)parameters).Cast<object>().Count();
                Lines += count;
                return count;
            }
            if (sql.Contains("UPDATE dbo.[Order]")) HeaderUpdates++;
            return 1;
        }

        public List<T> Query<T, U>(string sql, U parameters)
        {
            Queries++;
            LastParameters = parameters;
            if (!ReturnMissingCatalogLine) return new List<T>();
            var row = Activator.CreateInstance(typeof(T), nonPublic: true);
            typeof(T).GetProperty("ProductId").SetValue(row, 495);
            typeof(T).GetProperty("OrderId").SetValue(row, (int?)42);
            typeof(T).GetProperty("Quantity").SetValue(row, 2);
            typeof(T).GetProperty("Price").SetValue(row, 10m);
            return new List<T> { (T)row };
        }

        public T QuerySingle<T, U>(string sql, U parameters)
        {
            LastParameters = parameters;
            AllWritesTransactional &= depth > 0;
            Headers++;
            persistedId = 42;
            return (T)(object)42;
        }

        public T QuerySingleOrDefault<T, U>(string sql, U parameters)
        {
            LastParameters = parameters;
            if (typeof(T) == typeof(int?) && persistedId.HasValue) return (T)(object)persistedId;
            return default;
        }

        public T InTransaction<T>(Func<IDataAccess, T> operation, IsolationLevel isolationLevel = IsolationLevel.Serializable)
        {
            var before = (Headers, Lines, HeaderUpdates, persistedId);
            depth++;
            try { return operation(this); }
            catch
            {
                (Headers, Lines, HeaderUpdates, persistedId) = before;
                Rollbacks++;
                throw;
            }
            finally { depth--; }
        }
    }
}
