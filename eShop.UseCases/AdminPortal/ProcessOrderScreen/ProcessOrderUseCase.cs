using eShop.CoreBusiness.Services;
using eShop.UseCases.PluginInterfaces.DataStore;
using System;

namespace eShop.UseCases.ProcessOrderScreen
{
    public class ProcessOrderUseCase : IProcessOrderUseCase
    {
        private readonly IOrderRepository orderRepository;

        public ProcessOrderUseCase(IOrderRepository orderRepository, IOrderService orderService)
        {
            this.orderRepository = orderRepository;
        }

        public bool Execute(int orderId, string adminUserName)
        {
            if (orderId <= 0 || string.IsNullOrWhiteSpace(adminUserName)
                || adminUserName.Trim().Length > 150) return false;
            return orderRepository.TryProcessOrder(orderId, adminUserName.Trim(), DateTime.UtcNow);
        }
    }
}
