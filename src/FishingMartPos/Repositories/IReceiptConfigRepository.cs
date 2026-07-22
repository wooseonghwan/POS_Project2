using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IReceiptConfigRepository
{
    Task<ReceiptConfig?> GetAsync(string posCd);
    Task SaveAsync(ReceiptConfig config);
}
