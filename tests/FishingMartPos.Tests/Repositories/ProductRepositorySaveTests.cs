using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class ProductRepositorySaveTests
{
    private static IProductRepository CreateRepository()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return new ProductRepository(factory);
    }

    [Fact]
    public async Task SaveAsync_InsertsNewProduct_ThenUpdatesOnSecondSave()
    {
        var repository = CreateRepository();
        const string barcode = "TEST_SAVE_0001";
        try
        {
            await repository.SaveAsync(new Product
            {
                Barcode = barcode, MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT",
                Name = "테스트상품", Price = 1000, StockQty = 5, PhotoPath = null,
            });

            var afterInsert = await repository.GetActiveAsync();
            var inserted = Assert.Single(afterInsert.Where(p => p.Barcode == barcode));
            Assert.Equal("테스트상품", inserted.Name);
            Assert.Null(inserted.PhotoPath);

            await repository.SaveAsync(new Product
            {
                Barcode = barcode, MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT",
                Name = "테스트상품(수정)", Price = 2000, StockQty = 9, PhotoPath = "ProductPhotos/TEST_SAVE_0001.jpg",
            });

            var afterUpdate = await repository.GetActiveAsync();
            var updated = Assert.Single(afterUpdate.Where(p => p.Barcode == barcode));
            Assert.Equal("테스트상품(수정)", updated.Name);
            Assert.Equal(2000, updated.Price);
            Assert.Equal("ProductPhotos/TEST_SAVE_0001.jpg", updated.PhotoPath);
        }
        finally
        {
            await repository.DeactivateAsync(barcode);
        }
    }
}
