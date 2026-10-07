using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ICashDrawerRepository
{
    Task<CashDrawerEntry?> GetAsync(string posCd, DateTime businessDate);
    Task SaveOpeningAmountAsync(CashDrawerEntry entry);
}
