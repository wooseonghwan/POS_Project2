using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeStaffRepository : IStaffRepository
{
    private readonly Dictionary<string, Staff> _staffByPin;

    public FakeStaffRepository(Dictionary<string, Staff> staffByPin)
    {
        _staffByPin = staffByPin;
    }

    public Task<Staff?> FindByPinAsync(string pin)
    {
        _staffByPin.TryGetValue(pin, out var staff);
        return Task.FromResult(staff);
    }
}
