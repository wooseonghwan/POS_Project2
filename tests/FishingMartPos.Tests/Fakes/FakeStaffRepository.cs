using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeStaffRepository : IStaffRepository
{
    private readonly Dictionary<string, Staff> _staffByPin;
    private readonly List<Staff> _allStaff;

    public FakeStaffRepository(Dictionary<string, Staff> staffByPin)
    {
        _staffByPin = staffByPin;
        _allStaff = staffByPin.Values.ToList();
    }

    public List<(Staff Staff, string Pin)> CreatedStaff { get; } = new();
    public List<(Staff Staff, string? NewPin)> UpdatedStaff { get; } = new();

    public Task<Staff?> FindByPinAsync(string pin)
    {
        _staffByPin.TryGetValue(pin, out var staff);
        return Task.FromResult(staff);
    }

    public Task<IReadOnlyList<Staff>> GetAllAsync() =>
        Task.FromResult((IReadOnlyList<Staff>)_allStaff.ToList());

    public Task CreateAsync(Staff staff, string pin)
    {
        CreatedStaff.Add((staff, pin));
        _allStaff.Add(staff);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Staff staff, string? newPin)
    {
        UpdatedStaff.Add((staff, newPin));
        _allStaff.RemoveAll(s => s.StaffCode == staff.StaffCode);
        _allStaff.Add(staff);
        return Task.CompletedTask;
    }
}
