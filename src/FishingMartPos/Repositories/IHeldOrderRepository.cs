using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IHeldOrderRepository
{
    Task<long> HoldAsync(string posCd, string staffCd, IReadOnlyList<HeldOrderLine> lines);
    Task<IReadOnlyList<(long HoldNo, DateTime HeldAt, decimal Total)>> GetHeldAsync(string posCd);
    Task<IReadOnlyList<HeldOrderLine>> GetLinesAsync(long holdNo);
    Task DeleteAsync(long holdNo);
}
