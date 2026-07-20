using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeProductRepository : IProductRepository
{
    private readonly IReadOnlyList<Product> _products;

    public FakeProductRepository(IReadOnlyList<Product> products)
    {
        _products = products;
    }

    public Task<IReadOnlyList<Product>> GetActiveAsync() => Task.FromResult(_products);
}
