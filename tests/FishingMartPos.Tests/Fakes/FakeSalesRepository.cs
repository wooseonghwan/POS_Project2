using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSalesRepository : ISalesRepository
{
    public List<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)> CreatedSales { get; } = new();
    public List<(DateTime From, DateTime To)> GetCompletedSalesCalls { get; } = new();
    private long _nextSaleNo = 1;
    private IReadOnlyList<SaleHeader> _completedSales = Array.Empty<SaleHeader>();

    public Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        CreatedSales.Add((header, lines));
        return Task.FromResult(_nextSaleNo++);
    }

    public void SeedCompletedSales(IReadOnlyList<SaleHeader> sales)
    {
        _completedSales = sales;
    }

    public Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to)
    {
        GetCompletedSalesCalls.Add((from, to));
        var filtered = _completedSales.Where(s => s.SaleDt >= from && s.SaleDt < to).ToList();
        return Task.FromResult<IReadOnlyList<SaleHeader>>(filtered);
    }
}
