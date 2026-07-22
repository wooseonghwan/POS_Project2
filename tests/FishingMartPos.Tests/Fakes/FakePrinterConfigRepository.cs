using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakePrinterConfigRepository : IPrinterConfigRepository
{
    private readonly Dictionary<string, PrinterConfig> _byPosCd;

    public FakePrinterConfigRepository(Dictionary<string, PrinterConfig>? byPosCd = null)
    {
        _byPosCd = byPosCd ?? new Dictionary<string, PrinterConfig>();
    }

    public List<PrinterConfig> SavedConfigs { get; } = new();

    public Task<PrinterConfig?> GetAsync(string posCd)
    {
        _byPosCd.TryGetValue(posCd, out var config);
        return Task.FromResult(config);
    }

    public Task SaveAsync(PrinterConfig config)
    {
        SavedConfigs.Add(config);
        _byPosCd[config.PosCd] = config;
        return Task.CompletedTask;
    }
}
