using Dapper;
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

    [Fact]
    public async Task DeactivateAsync_SetsUseYnToN()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        IProductRepository repository = new ProductRepository(factory);
        const string barcode = "8800000020001";

        try
        {
            await repository.DeactivateAsync(barcode);

            using var connection = await factory.CreateOpenConnectionAsync();
            string useYn = await connection.QuerySingleAsync<string>(
                "SELECT use_yn FROM product_tb WHERE barcode = @Barcode", new { Barcode = barcode });
            Assert.Equal("N", useYn);
        }
        finally
        {
            // 정리: 다른 테스트(GetActive 등)에 영향 주지 않도록 원복
            using var connection = await factory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(
                "UPDATE product_tb SET use_yn = 'Y' WHERE barcode = @Barcode", new { Barcode = barcode });
        }
    }
}
