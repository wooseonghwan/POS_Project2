using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class ProductRepositoryTests
{
    [Fact]
    public async Task GetActive_ReturnsSeededFortyThreeProducts()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        IProductRepository repository = new ProductRepository(factory);

        var products = await repository.GetActiveAsync();

        Assert.Contains(products, p => p.Barcode == "8800000020001" && p.Name == "지렁이" && p.PosCatCd == "BAIT");
        Assert.True(products.Count >= 43);
    }
}
