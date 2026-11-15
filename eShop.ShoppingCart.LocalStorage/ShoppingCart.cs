using eShop.CoreBusiness.Models;
using eShop.UseCases.PluginInterfaces.DataStore;
using eShop.UseCases.PluginInterfaces.UI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace eShop.ShoppingCart.LocalStorage
{
    /// <summary>Only product identifiers, quantities and a protected checkout token leave the server.</summary>
    public sealed class ShoppingCart : IShoppingCart, IAsyncDisposable
    {
        private readonly IJSRuntime jsRuntime;
        private readonly IProductRepository productRepository;
        private readonly IDataProtector checkoutProtector;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private IJSObjectReference module;

        public ShoppingCart(IJSRuntime jsRuntime, IProductRepository productRepository,
            IDataProtectionProvider protectionProvider)
        {
            this.jsRuntime = jsRuntime;
            this.productRepository = productRepository;
            checkoutProtector = protectionProvider.CreateProtector("MyShop.Basket.Checkout.v1");
        }

        public Task<Order> GetOrderAsync() => LockedAsync(ReadOrderAsync);

        public Task<Order> AddProductAsync(Product product)
        {
            if (product == null || product.ProductId <= 0) throw new ArgumentException("Choose an available product.", nameof(product));
            return LockedAsync(async () =>
            {
                await OperateAsync("add", new { productId = product.ProductId, quantity = 1 });
                return await ReadOrderAsync();
            });
        }

        public Task<Order> DeleteProductAsync(int productId) => LockedAsync(async () =>
        {
            await OperateAsync("remove", new { productId });
            return await ReadOrderAsync();
        });

        public Task<Order> UpdateQuantityAsync(int productId, int quantity)
        {
            if (quantity < 0 || quantity > Order.MaximumQuantity)
                throw new ArgumentOutOfRangeException(nameof(quantity), $"Choose between 0 and {Order.MaximumQuantity} units.");
            return LockedAsync(async () =>
            {
                await OperateAsync("quantity", new { productId, quantity });
                return await ReadOrderAsync();
            });
        }

        public async Task UpdateOrderAsync(Order order)
        {
            await LockedAsync(async () =>
            {
                var items = order?.LineItems?.Where(item => item != null)
                    .Select(item => new CartItem { ProductId = item.ProductId, Quantity = item.Quantity }).ToList()
                    ?? new List<CartItem>();
                await OperateAsync("replace", new { items });
                return true;
            });
        }

        public async Task EmptyAsync(string checkoutId = null)
        {
            await LockedAsync(async () =>
            {
                var document = await OperateAsync("read");
                if (checkoutId == null || string.Equals(ReadCheckoutId(document), checkoutId, StringComparison.Ordinal))
                    await OperateAsync("clear", new { checkoutToken = document.CheckoutToken });
                return true;
            });
        }

        private async Task<Order> ReadOrderAsync()
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var document = await OperateAsync("read");
                document.Items ??= new List<CartItem>();
                var order = new Order();
                var missing = new List<int>();
                foreach (var item in document.Items)
                {
                    if (item == null || item.ProductId <= 0 || item.Quantity <= 0 || item.Quantity > Order.MaximumQuantity)
                        continue;
                    var product = productRepository.GetProduct(item.ProductId);
                    if (product == null || product.Price <= 0 || product.Price > 1000000m
                        || decimal.Round(product.Price, 2, MidpointRounding.AwayFromZero) <= 0)
                    {
                        missing.Add(item.ProductId);
                        continue;
                    }
                    order.AddProduct(product.ProductId, item.Quantity, decimal.Round(product.Price, 2, MidpointRounding.AwayFromZero), product);
                }

                if (missing.Count > 0)
                {
                    await OperateAsync("prune", new { productIds = missing });
                    continue;
                }

                var checkoutId = ReadCheckoutId(document);
                if (checkoutId == null)
                {
                    var id = Guid.NewGuid().ToString("D");
                    var token = checkoutProtector.Protect(id + "|" + Fingerprint(document));
                    var updated = await OperateAsync("token", new
                    {
                        revision = document.Revision,
                        fingerprint = Fingerprint(document),
                        checkoutToken = token
                    });
                    checkoutId = ReadCheckoutId(updated);
                    if (checkoutId == null || Fingerprint(updated) != Fingerprint(document)) continue;
                }

                order.UniqueId = checkoutId;
                return order;
            }

            throw new InvalidOperationException("Your basket changed in another tab. Please try again.");
        }

        private string ReadCheckoutId(CartDocument document)
        {
            if (string.IsNullOrWhiteSpace(document.CheckoutToken) || document.CheckoutToken.Length > 8192) return null;
            try
            {
                var value = checkoutProtector.Unprotect(document.CheckoutToken);
                var separator = value.IndexOf('|');
                if (separator <= 0 || !Guid.TryParse(value.Substring(0, separator), out var id)
                    || value.Substring(separator + 1) != Fingerprint(document)) return null;
                return id.ToString("D");
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private static string Fingerprint(CartDocument document)
            => string.Join(",", (document.Items ?? new List<CartItem>()).Where(item => item != null)
                .OrderBy(item => item.ProductId).Select(item => $"{item.ProductId}:{item.Quantity}"));

        private async Task<CartDocument> OperateAsync(string operation, object payload = null)
        {
            module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/eShop.ShoppingCart.LocalStorage/myShopCart.js");
            return await module.InvokeAsync<CartDocument>("operate", operation, payload);
        }

        private async Task<T> LockedAsync<T>(Func<Task<T>> action)
        {
            await gate.WaitAsync();
            try { return await action(); }
            finally { gate.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            if (module != null)
            {
                try { await module.DisposeAsync(); }
                catch (JSDisconnectedException) { }
            }
            gate.Dispose();
        }

        public sealed class CartDocument
        {
            public int Version { get; set; } = 1;
            public int Revision { get; set; }
            public string CheckoutToken { get; set; }
            public List<CartItem> Items { get; set; } = new List<CartItem>();
        }

        public sealed class CartItem
        {
            public int ProductId { get; set; }
            public int Quantity { get; set; }
        }
    }
}
