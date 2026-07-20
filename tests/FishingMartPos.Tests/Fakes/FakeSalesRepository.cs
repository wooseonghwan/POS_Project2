using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSalesRepository : ISalesRepository
{
    public List<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)> CreatedSales { get; } = new();
    private long _nextSaleNo = 1;

    public Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        CreatedSales.Add((header, lines));
        return Task.FromResult(_nextSaleNo++);
    }
}
