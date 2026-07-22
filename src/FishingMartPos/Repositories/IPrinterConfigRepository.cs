using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IPrinterConfigRepository
{
    Task<PrinterConfig?> GetAsync(string posCd);
    Task SaveAsync(PrinterConfig config);
}
