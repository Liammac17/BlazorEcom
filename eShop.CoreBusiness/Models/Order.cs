using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace eShop.CoreBusiness.Models
{
    public class Order
    {
        public const int MaximumQuantity = 99;
        public const int MaximumLines = 100;
        public Order()
        {
            LineItems = new List<OrderLineItem>();
        }

        public int? OrderId { get; set; }
        public DateTime? DatePlaced { get; set; }
        public DateTime? DateProcessing { get; set; }
        public DateTime? DateProcessed { get; set; }
        public string CustomerName { get; set; }
        public string CustomerAddress { get; set; }
        public string CustomerCity { get; set; }
        public string CustomerStateProvince { get; set; }
        public string CustomerCountry { get; set; }
        public string AdminUser { get; set; }
        public List<OrderLineItem> LineItems { get; set; }
        public string UniqueId { get; set; }

        public void AddProduct(int productId, int qty, decimal price, Product product = null)
        {
            if (productId <= 0) throw new ArgumentOutOfRangeException(nameof(productId));
            if (qty <= 0 || qty > MaximumQuantity) throw new ArgumentOutOfRangeException(nameof(qty));
            if (price <= 0 || price > 1000000m) throw new ArgumentOutOfRangeException(nameof(price));

            var item = LineItems.FirstOrDefault(x => x.ProductId == productId);
            if (item != null)
            {
                if (item.Quantity > MaximumQuantity - qty)
                    throw new ArgumentOutOfRangeException(nameof(qty), $"A product is limited to {MaximumQuantity} units.");
                item.Quantity += qty;
                item.Price = price;
                item.Product = product ?? item.Product;
            }
            else
            {
                if (LineItems.Count >= MaximumLines)
                    throw new InvalidOperationException($"A basket is limited to {MaximumLines} products.");
                LineItems.Add(new OrderLineItem { ProductId = productId, Quantity = qty, Price = price, OrderId = OrderId, Product = product });
            }
        }

        public void RemoveProduct(int productId)
        {
            LineItems.RemoveAll(item => item.ProductId == productId);
        }

    }
}
