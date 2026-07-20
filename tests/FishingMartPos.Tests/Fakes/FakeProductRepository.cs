using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeProductRepository : IProductRepository
{
    private readonly List<Product> _products;

    public FakeProductRepository(IReadOnlyList<Product> products)
    {
        _products = products.ToList();
    }

    public List<string> DeactivatedBarcodes { get; } = new();

    public Task<IReadOnlyList<Product>> GetActiveAsync() =>
        Task.FromResult((IReadOnlyList<Product>)_products);

    public Task DeactivateAsync(string barcode)
    {
        DeactivatedBarcodes.Add(barcode);
        _products.RemoveAll(p => p.Barcode == barcode);
        return Task.CompletedTask;
    }
}
