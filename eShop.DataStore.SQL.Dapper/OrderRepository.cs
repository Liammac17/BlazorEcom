using eShop.CoreBusiness.Models;
using eShop.UseCases.PluginInterfaces.DataStore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace eShop.DataStore.SQL.Dapper
{
    public class OrderRepository : IOrderRepository
    {
        private const string OrderColumns = "OrderId, DatePlaced, DateProcessing, DateProcessed, CustomerName, CustomerAddress, CustomerCity, CustomerStateProvince, CustomerCountry, AdminUser, UniqueId";
        private readonly IDataAccess dataAccess;

        public OrderRepository(IDataAccess dataAccess)
        {
            this.dataAccess = dataAccess;
        }

        public int CreateOrder(Order order)
        {
            ArgumentNullException.ThrowIfNull(order);
            if (!Guid.TryParse(order.UniqueId, out _) || order.LineItems == null || order.LineItems.Count == 0)
                throw new ArgumentException("An order requires a checkout identifier and line items.", nameof(order));

            var orderId = dataAccess.InTransaction(session =>
            {
                // A serializable update-range lock makes retries atomic even on the original schema.
                // The optional UniqueId index in docs/sql improves this lookup for larger databases.
                var existingId = session.QuerySingleOrDefault<int?, object>(
                    "SELECT OrderId FROM dbo.[Order] WITH (UPDLOCK, HOLDLOCK) WHERE UniqueId = @UniqueId",
                    new { order.UniqueId });
                if (existingId.HasValue) return existingId.Value;

                // Fulfillment state is owned by the server, never accepted from the checkout payload.
                var id = session.QuerySingle<int, object>(@"
                    INSERT INTO dbo.[Order]
                        (DatePlaced, CustomerName, CustomerAddress, CustomerCity, CustomerStateProvince, CustomerCountry, UniqueId)
                    OUTPUT INSERTED.OrderId
                    VALUES
                        (@DatePlaced, @CustomerName, @CustomerAddress, @CustomerCity, @CustomerStateProvince, @CustomerCountry, @UniqueId)",
                    new
                    {
                        DatePlaced = order.DatePlaced ?? DateTime.UtcNow,
                        order.CustomerName,
                        order.CustomerAddress,
                        order.CustomerCity,
                        order.CustomerStateProvince,
                        order.CustomerCountry,
                        order.UniqueId
                    });

                var rows = order.LineItems.Select(item => new
                {
                    item.ProductId,
                    OrderId = id,
                    item.Quantity,
                    item.Price
                }).ToArray();
                var inserted = session.ExecuteCommand(@"
                    INSERT INTO dbo.OrderLineItem (ProductId, OrderId, Quantity, Price)
                    VALUES (@ProductId, @OrderId, @Quantity, @Price)", rows);
                if (inserted != rows.Length)
                    throw new InvalidOperationException("The complete order could not be saved.");
                return id;
            });

            order.OrderId = orderId;
            foreach (var item in order.LineItems) item.OrderId = orderId;
            return orderId;
        }

        public IEnumerable<OrderLineItem> GetLineItemsByOrderId(int orderId)
        {
            if (orderId <= 0) return Array.Empty<OrderLineItem>();
            var rows = dataAccess.Query<OrderLineItemRow, object>(@"
                SELECT li.LineItemId, li.ProductId, li.OrderId, li.Quantity, li.Price,
                       p.Brand AS ProductBrand, p.Name AS ProductName, p.Price AS ProductPrice,
                       p.ImageLink AS ProductImageLink, p.Description AS ProductDescription
                FROM dbo.OrderLineItem li
                LEFT JOIN dbo.Product p ON p.ProductId = li.ProductId
                WHERE li.OrderId = @OrderId
                ORDER BY li.LineItemId", new { OrderId = orderId });

            return rows.Select(row => new OrderLineItem
            {
                LineItemId = row.LineItemId,
                ProductId = row.ProductId,
                OrderId = row.OrderId,
                Quantity = row.Quantity,
                Price = row.Price,
                Product = new Product
                {
                    ProductId = row.ProductId,
                    Brand = row.ProductBrand ?? "Unavailable",
                    Name = row.ProductName ?? $"Unavailable product #{row.ProductId}",
                    Price = row.ProductPrice ?? row.Price,
                    ImageLink = row.ProductImageLink,
                    Description = row.ProductDescription
                }
            }).ToList();
        }

        public Order GetOrder(int id)
        {
            if (id <= 0) return null;
            var order = dataAccess.QuerySingleOrDefault<Order, object>(
                $"SELECT {OrderColumns} FROM dbo.[Order] WHERE OrderId = @OrderId", new { OrderId = id });
            return PopulateLines(order);
        }

        public Order GetOrderByUniqueId(string uniqueId)
        {
            if (!Guid.TryParse(uniqueId, out _)) return null;
            var order = dataAccess.QuerySingleOrDefault<Order, object>(
                $"SELECT {OrderColumns} FROM dbo.[Order] WHERE UniqueId = @UniqueId", new { UniqueId = uniqueId });
            return PopulateLines(order);
        }

        public IEnumerable<Order> GetOrders()
            => dataAccess.Query<Order, object>($"SELECT {OrderColumns} FROM dbo.[Order] ORDER BY DatePlaced DESC, OrderId DESC", new { });

        public IEnumerable<Order> GetOutstandingOrders()
            => dataAccess.Query<Order, object>($"SELECT {OrderColumns} FROM dbo.[Order] WHERE DateProcessed IS NULL ORDER BY DatePlaced, OrderId", new { });

        public IEnumerable<Order> GetProcessedOrders()
            => dataAccess.Query<Order, object>($"SELECT {OrderColumns} FROM dbo.[Order] WHERE DateProcessed IS NOT NULL ORDER BY DateProcessed DESC, OrderId DESC", new { });

        public bool TryProcessOrder(int orderId, string adminUserName, DateTime processedAt)
        {
            if (orderId <= 0 || string.IsNullOrWhiteSpace(adminUserName) || adminUserName.Length > 150)
                return false;

            return dataAccess.ExecuteCommand(@"
                UPDATE dbo.[Order]
                SET DateProcessed = @DateProcessed, AdminUser = @AdminUser
                WHERE OrderId = @OrderId AND DateProcessed IS NULL",
                new { OrderId = orderId, DateProcessed = processedAt, AdminUser = adminUserName }) == 1;
        }

        public void UpdateOrder(Order order)
        {
            ArgumentNullException.ThrowIfNull(order);
            if (!order.OrderId.HasValue || order.LineItems == null)
                throw new ArgumentException("A persisted order and its line items are required.", nameof(order));

            dataAccess.InTransaction(session =>
            {
                var updated = session.ExecuteCommand(@"
                    UPDATE dbo.[Order]
                    SET DatePlaced = @DatePlaced, DateProcessing = @DateProcessing, DateProcessed = @DateProcessed,
                        CustomerName = @CustomerName, CustomerAddress = @CustomerAddress, CustomerCity = @CustomerCity,
                        CustomerStateProvince = @CustomerStateProvince, CustomerCountry = @CustomerCountry,
                        AdminUser = @AdminUser, UniqueId = @UniqueId
                    WHERE OrderId = @OrderId", order);
                if (updated != 1) throw new KeyNotFoundException("The order no longer exists.");

                foreach (var item in order.LineItems)
                {
                    if (item.OrderId != order.OrderId || !item.LineItemId.HasValue)
                        throw new ArgumentException("A line item does not belong to this order.", nameof(order));
                    var lineUpdated = session.ExecuteCommand(@"
                        UPDATE dbo.OrderLineItem
                        SET ProductId = @ProductId, Quantity = @Quantity, Price = @Price
                        WHERE LineItemId = @LineItemId AND OrderId = @OrderId", item);
                    if (lineUpdated != 1) throw new KeyNotFoundException("An order line no longer exists.");
                }
                return true;
            });
        }

        private Order PopulateLines(Order order)
        {
            if (order != null)
                order.LineItems = GetLineItemsByOrderId(order.OrderId.Value).ToList();
            return order;
        }

        private sealed class OrderLineItemRow : OrderLineItem
        {
            public string ProductBrand { get; set; }
            public string ProductName { get; set; }
            public decimal? ProductPrice { get; set; }
            public string ProductImageLink { get; set; }
            public string ProductDescription { get; set; }
        }
    }
}
