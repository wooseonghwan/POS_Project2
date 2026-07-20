using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ISalesRepository
{
    Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines);
}
