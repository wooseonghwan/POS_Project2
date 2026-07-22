using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ISystemInfoRepository
{
    Task<SystemInfo?> GetAsync();
}
