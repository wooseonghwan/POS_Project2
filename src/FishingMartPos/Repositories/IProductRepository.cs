using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetActiveAsync();
    Task DeactivateAsync(string barcode);
    Task SaveAsync(Product product);

    /// <summary>같은 POS분류 안에서 상품이 노출되는 순서를 전달받은 순서대로 0,1,2...로 다시 매겨 저장한다.</summary>
    Task UpdateSortOrderAsync(string posCatCd, IReadOnlyList<string> orderedBarcodes);
}
