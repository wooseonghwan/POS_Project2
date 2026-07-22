using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IStaffRepository
{
    Task<Staff?> FindByPinAsync(string pin);
    Task<IReadOnlyList<Staff>> GetAllAsync();
    Task CreateAsync(Staff staff, string pin);
    Task UpdateAsync(Staff staff, string? newPin);
}
