using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IVanConfigRepository
{
    Task<IReadOnlyList<VanConfigRow>> GetByPosCodeAsync(string posCd);
}
