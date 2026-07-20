using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IStaffRepository
{
    Task<Staff?> FindByPinAsync(string pin);
}
