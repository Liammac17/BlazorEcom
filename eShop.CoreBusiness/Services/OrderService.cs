using eShop.CoreBusiness.Models;
using System;
using System.Linq;

namespace eShop.CoreBusiness.Services
{
    public class OrderService : IOrderService
    {
        public bool ValidateCustomerInformation(string name, string address, string city, string province, string country)
            => HasText(name, 150) && HasText(address, 150) && HasText(city, 50)
                && HasText(province, 50) && HasText(country, 50);

        public bool ValidateCreateOrder(Order order)
            => order != null && !order.OrderId.HasValue && order.DatePlaced.HasValue
                && !order.DateProcessing.HasValue && !order.DateProcessed.HasValue
                && string.IsNullOrWhiteSpace(order.AdminUser)
                && Guid.TryParse(order.UniqueId, out _)
                && ValidLines(order)
                && ValidateCustomerInformation(order.CustomerName, order.CustomerAddress,
                    order.CustomerCity, order.CustomerStateProvince, order.CustomerCountry);

        public bool ValidateUpdateOrder(Order order)
            => order != null && order.OrderId > 0 && order.DatePlaced.HasValue
                && !order.DateProcessing.HasValue && !order.DateProcessed.HasValue
                && Guid.TryParse(order.UniqueId, out _) && ValidLines(order)
                && order.LineItems.All(item => item.OrderId == order.OrderId)
                && ValidateCustomerInformation(order.CustomerName, order.CustomerAddress,
                    order.CustomerCity, order.CustomerStateProvince, order.CustomerCountry);

        public bool ValidateProcessOrder(Order order)
            => order != null && order.OrderId > 0 && order.DateProcessed.HasValue
                && HasText(order.AdminUser, 150);

        private static bool ValidLines(Order order)
            => order.LineItems != null && order.LineItems.Count > 0
                && order.LineItems.Count <= Order.MaximumLines
                && order.LineItems.All(item => item != null && item.ProductId > 0
                    && item.Price > 0 && item.Price <= 1000000m
                    && item.Quantity > 0 && item.Quantity <= Order.MaximumQuantity)
                && order.LineItems.Select(item => item.ProductId).Distinct().Count() == order.LineItems.Count;

        private static bool HasText(string value, int maximum)
            => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum;
    }
}
