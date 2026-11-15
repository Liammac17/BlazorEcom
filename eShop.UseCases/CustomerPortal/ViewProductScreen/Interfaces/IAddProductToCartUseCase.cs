namespace eShop.UseCases.ViewProductScreen
{
    public interface IAddProductToCartUseCase
    {
        System.Threading.Tasks.Task Execute(int productId);
    }
}
