using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeCashDrawerRepository : ICashDrawerRepository
{
    private readonly Dictionary<(string PosCd, DateTime BusinessDate), CashDrawerEntry> _entries = new();

    public List<CashDrawerEntry> SavedEntries { get; } = new();

    public FakeCashDrawerRepository(IEnumerable<CashDrawerEntry>? seed = null)
    {
        foreach (var entry in seed ?? Enumerable.Empty<CashDrawerEntry>())
        {
            _entries[(entry.PosCd, entry.BusinessDate.Date)] = entry;
        }
    }

    public Task<CashDrawerEntry?> GetAsync(string posCd, DateTime businessDate) =>
        Task.FromResult(_entries.TryGetValue((posCd, businessDate.Date), out var entry) ? entry : null);

    public Task SaveOpeningAmountAsync(CashDrawerEntry entry)
    {
        SavedEntries.Add(entry);
        _entries[(entry.PosCd, entry.BusinessDate.Date)] = entry;
        return Task.CompletedTask;
    }
}
