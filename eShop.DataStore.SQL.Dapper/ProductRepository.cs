using eShop.CoreBusiness.Models;
using eShop.UseCases.PluginInterfaces.DataStore;
using System.Collections.Generic;

namespace eShop.DataStore.SQL.Dapper
{
    public class ProductRepository : IProductRepository
    {
        private const string Columns = "ProductId, Brand, Name, Price, ImageLink, Description";
        private readonly IDataAccess dataAccess;

        public ProductRepository(IDataAccess dataAccess)
        {
            this.dataAccess = dataAccess;
        }

        public Product GetProduct(int id)
        {
            if (id <= 0) return null;
            return dataAccess.QuerySingleOrDefault<Product, object>(
                $"SELECT {Columns} FROM dbo.Product WHERE ProductId = @ProductId", new { ProductId = id });
        }

        public IEnumerable<Product> GetProducts(string filter = null)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return dataAccess.Query<Product, object>($"SELECT {Columns} FROM dbo.Product ORDER BY Name, ProductId", new { });

            // Treat SQL LIKE wildcard characters as literal search text.
            var escaped = filter.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
            return dataAccess.Query<Product, object>(
                $"SELECT {Columns} FROM dbo.Product WHERE Name LIKE @Filter ESCAPE '~' OR Brand LIKE @Filter ESCAPE '~' ORDER BY Name, ProductId",
                new { Filter = "%" + escaped + "%" });
        }
    }
}
