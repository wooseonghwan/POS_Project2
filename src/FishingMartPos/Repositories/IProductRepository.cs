using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetActiveAsync();
    Task DeactivateAsync(string barcode);
    Task SaveAsync(Product product);
}
