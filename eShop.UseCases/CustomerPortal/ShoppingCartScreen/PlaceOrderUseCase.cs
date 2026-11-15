using eShop.CoreBusiness.Models;
using eShop.CoreBusiness.Services;
using eShop.UseCases.PluginInterfaces.DataStore;
using eShop.UseCases.PluginInterfaces.StateStore;
using eShop.UseCases.PluginInterfaces.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace eShop.UseCases.ShoppingCartScreen
{
    public class PlaceOrderUseCase : IPlaceOrderUseCase
    {
        private readonly IShoppingCart shoppingCart;
        private readonly IOrderRepository orderRepository;
        private readonly IOrderService orderService;
        private readonly IShoppingCartStateStore shoppingCartStateStore;
        private readonly IProductRepository productRepository;

        public PlaceOrderUseCase(IShoppingCart shoppingCart, IOrderRepository orderRepository,
            IOrderService orderService, IShoppingCartStateStore shoppingCartStateStore,
            IProductRepository productRepository)
        {
            this.shoppingCart = shoppingCart;
            this.orderRepository = orderRepository;
            this.orderService = orderService;
            this.shoppingCartStateStore = shoppingCartStateStore;
            this.productRepository = productRepository;
        }

        public async Task<string> Execute(Order proposedOrder)
        {
            if (proposedOrder == null) return null;

            // The adapter verifies a protected checkout token. Never accept an arbitrary
            // browser-supplied confirmation GUID as an idempotency key.
            var cart = await shoppingCart.GetOrderAsync();
            if (cart?.LineItems == null || cart.LineItems.Count == 0
                || !Guid.TryParse(cart.UniqueId, out _)
                || !string.Equals(cart.UniqueId, proposedOrder.UniqueId, StringComparison.Ordinal))
                return null;

            // The submitted prices are a consent check, never the source of persisted prices.
            // If catalog prices changed after the checkout screen loaded, require a fresh
            // review instead of silently charging a different total.
            if (proposedOrder.LineItems == null || proposedOrder.LineItems.Count != cart.LineItems.Count)
                return null;
            var cartLines = cart.LineItems.ToDictionary(item => item.ProductId);
            var submittedIds = new HashSet<int>();
            foreach (var submitted in proposedOrder.LineItems)
            {
                if (submitted == null || !submittedIds.Add(submitted.ProductId)
                    || !cartLines.TryGetValue(submitted.ProductId, out var current)
                    || submitted.Quantity != current.Quantity || submitted.Price != current.Price)
                    return null;
            }

            // Only customer fields and server-rehydrated basket lines may enter an order.
            var order = new Order
            {
                UniqueId = cart.UniqueId,
                DatePlaced = DateTime.UtcNow,
                CustomerName = proposedOrder.CustomerName?.Trim(),
                CustomerAddress = proposedOrder.CustomerAddress?.Trim(),
                CustomerCity = proposedOrder.CustomerCity?.Trim(),
                CustomerStateProvince = proposedOrder.CustomerStateProvince?.Trim(),
                CustomerCountry = proposedOrder.CustomerCountry?.Trim()
            };

            foreach (var item in cart.LineItems)
            {
                var product = productRepository.GetProduct(item.ProductId);
                if (product == null || item.Quantity <= 0 || item.Quantity > Order.MaximumQuantity
                    || product.Price <= 0 || product.Price > 1000000m
                    || decimal.Round(product.Price, 2, MidpointRounding.AwayFromZero) <= 0
                    || decimal.Round(product.Price, 2, MidpointRounding.AwayFromZero) != item.Price)
                    return null;
                order.AddProduct(product.ProductId, item.Quantity, decimal.Round(product.Price, 2, MidpointRounding.AwayFromZero), product);
            }

            if (!orderService.ValidateCreateOrder(order)) return null;

            // A retry uses the same protected cart identity; repository creation is idempotent.
            // No extra database read is needed after the transaction has committed.
            orderRepository.CreateOrder(order);
            await shoppingCart.EmptyAsync(order.UniqueId);
            shoppingCartStateStore.LineItemsCountUpdated();
            return order.UniqueId;
        }
    }
}
