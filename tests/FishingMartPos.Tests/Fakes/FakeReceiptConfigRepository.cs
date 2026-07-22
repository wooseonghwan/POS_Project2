using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeReceiptConfigRepository : IReceiptConfigRepository
{
    private readonly Dictionary<string, ReceiptConfig> _byPosCd;

    public FakeReceiptConfigRepository(Dictionary<string, ReceiptConfig>? byPosCd = null)
    {
        _byPosCd = byPosCd ?? new Dictionary<string, ReceiptConfig>();
    }

    public List<ReceiptConfig> SavedConfigs { get; } = new();

    public Task<ReceiptConfig?> GetAsync(string posCd)
    {
        _byPosCd.TryGetValue(posCd, out var config);
        return Task.FromResult(config);
    }

    public Task SaveAsync(ReceiptConfig config)
    {
        SavedConfigs.Add(config);
        _byPosCd[config.PosCd] = config;
        return Task.CompletedTask;
    }
}
