using eShop.CoreBusiness.Models;
using eShop.UseCases.PluginInterfaces.DataStore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace eShop.DataStore.HardCoded
{
    public class OrderRepository : IOrderRepository
    {
        private readonly Dictionary<int, Order> orders = new();
        private readonly object sync = new();
        private int nextOrderId = 1;

        public int CreateOrder(Order order)
        {
            ArgumentNullException.ThrowIfNull(order);
            if (!Guid.TryParse(order.UniqueId, out _) || order.LineItems == null || order.LineItems.Count == 0)
                throw new ArgumentException("A checkout identifier and line items are required.", nameof(order));
            lock (sync)
            {
                var existing = orders.Values.FirstOrDefault(x => x.UniqueId == order.UniqueId);
                if (existing != null)
                {
                    order.OrderId = existing.OrderId;
                    return existing.OrderId.Value;
                }
                var copy = Copy(order);
                copy.OrderId = nextOrderId++;
                copy.DateProcessing = null;
                copy.DateProcessed = null;
                copy.AdminUser = null;
                foreach (var item in copy.LineItems) item.OrderId = copy.OrderId;
                orders.Add(copy.OrderId.Value, copy);
                order.OrderId = copy.OrderId;
                foreach (var item in order.LineItems) item.OrderId = copy.OrderId;
                return copy.OrderId.Value;
            }
        }

        public IEnumerable<Order> GetOrders()
        {
            lock (sync) return orders.Values.OrderByDescending(x => x.DatePlaced).ThenByDescending(x => x.OrderId).Select(Copy).ToList();
        }

        public IEnumerable<Order> GetOutstandingOrders()
            => GetOrders().Where(x => !x.DateProcessed.HasValue).OrderBy(x => x.DatePlaced).ThenBy(x => x.OrderId).ToList();

        public IEnumerable<Order> GetProcessedOrders()
            => GetOrders().Where(x => x.DateProcessed.HasValue).OrderByDescending(x => x.DateProcessed).ThenByDescending(x => x.OrderId).ToList();

        public Order GetOrder(int id)
        {
            lock (sync) return orders.TryGetValue(id, out var order) ? Copy(order) : null;
        }

        public Order GetOrderByUniqueId(string uniqueId)
        {
            lock (sync)
            {
                var order = orders.Values.FirstOrDefault(x => x.UniqueId == uniqueId);
                return order == null ? null : Copy(order);
            }
        }

        public void UpdateOrder(Order order)
        {
            ArgumentNullException.ThrowIfNull(order);
            lock (sync)
            {
                if (!order.OrderId.HasValue || !orders.ContainsKey(order.OrderId.Value))
                    throw new KeyNotFoundException("The order no longer exists.");
                if (order.LineItems == null || order.LineItems.Any(x => x.OrderId != order.OrderId))
                    throw new ArgumentException("A line item does not belong to this order.", nameof(order));
                orders[order.OrderId.Value] = Copy(order);
            }
        }

        public bool TryProcessOrder(int orderId, string adminUserName, DateTime processedAt)
        {
            if (string.IsNullOrWhiteSpace(adminUserName) || adminUserName.Length > 150) return false;
            lock (sync)
            {
                if (!orders.TryGetValue(orderId, out var order) || order.DateProcessed.HasValue) return false;
                order.AdminUser = adminUserName;
                order.DateProcessed = processedAt;
                return true;
            }
        }

        public IEnumerable<OrderLineItem> GetLineItemsByOrderId(int orderId)
            => GetOrder(orderId)?.LineItems ?? new List<OrderLineItem>();

        private static Order Copy(Order order)
            => new Order
            {
                OrderId = order.OrderId,
                DatePlaced = order.DatePlaced,
                DateProcessing = order.DateProcessing,
                DateProcessed = order.DateProcessed,
                CustomerName = order.CustomerName,
                CustomerAddress = order.CustomerAddress,
                CustomerCity = order.CustomerCity,
                CustomerStateProvince = order.CustomerStateProvince,
                CustomerCountry = order.CustomerCountry,
                AdminUser = order.AdminUser,
                UniqueId = order.UniqueId,
                LineItems = order.LineItems.Select(item => new OrderLineItem
                {
                    LineItemId = item.LineItemId,
                    ProductId = item.ProductId,
                    OrderId = item.OrderId,
                    Quantity = item.Quantity,
                    Price = item.Price,
                    Product = item.Product == null ? null : new Product
                    {
                        ProductId = item.Product.ProductId,
                        Brand = item.Product.Brand,
                        Name = item.Product.Name,
                        Price = item.Product.Price,
                        ImageLink = item.Product.ImageLink,
                        Description = item.Product.Description
                    }
                }).ToList()
            };
    }
}
