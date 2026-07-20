using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeHeldOrderRepository : IHeldOrderRepository
{
    private readonly Dictionary<long, (string PosCd, List<HeldOrderLine> Lines, DateTime HeldAt)> _held = new();
    private long _nextHoldNo = 1;

    public Task<long> HoldAsync(string posCd, string staffCd, IReadOnlyList<HeldOrderLine> lines)
    {
        long holdNo = _nextHoldNo++;
        _held[holdNo] = (posCd, lines.ToList(), DateTime.Now);
        return Task.FromResult(holdNo);
    }

    public Task<IReadOnlyList<(long HoldNo, DateTime HeldAt, decimal Total)>> GetHeldAsync(string posCd)
    {
        var result = _held
            .Where(kv => kv.Value.PosCd == posCd)
            .Select(kv => (kv.Key, kv.Value.HeldAt, kv.Value.Lines.Sum(l => l.Qty * l.UnitPrice)))
            .ToList();
        return Task.FromResult<IReadOnlyList<(long, DateTime, decimal)>>(result);
    }

    public Task<IReadOnlyList<HeldOrderLine>> GetLinesAsync(long holdNo) =>
        Task.FromResult<IReadOnlyList<HeldOrderLine>>(_held[holdNo].Lines);

    public Task DeleteAsync(long holdNo)
    {
        _held.Remove(holdNo);
        return Task.CompletedTask;
    }
}
