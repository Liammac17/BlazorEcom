using eShop.UseCases.PluginInterfaces.DataStore;
using eShop.UseCases.PluginInterfaces.StateStore;
using eShop.UseCases.PluginInterfaces.UI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace eShop.UseCases.ViewProductScreen
{
    public class AddProductToCartUseCase : IAddProductToCartUseCase
    {
        private readonly IShoppingCart shoppingCart;
        private readonly IProductRepository productRepository;
        private readonly IShoppingCartStateStore shoppingCartStateStore;

        public AddProductToCartUseCase(
            IShoppingCart shoppingCart, 
            IProductRepository productRepository,
            IShoppingCartStateStore shoppingCartStateStore)
        {
            this.shoppingCart = shoppingCart;
            this.productRepository = productRepository;
            this.shoppingCartStateStore = shoppingCartStateStore;
        }

        public async Task Execute(int productId)
        {
            var product = productRepository.GetProduct(productId);
            if (product == null) throw new InvalidOperationException("This product is no longer available.");
            await shoppingCart.AddProductAsync(product);

            shoppingCartStateStore.LineItemsCountUpdated();
        }
    }
}
