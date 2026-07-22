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
        _staffByPin[pin] = staff;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Staff staff, string? newPin)
    {
        UpdatedStaff.Add((staff, newPin));
        _allStaff.RemoveAll(s => s.StaffCode == staff.StaffCode);
        _allStaff.Add(staff);

        if (!string.IsNullOrWhiteSpace(newPin))
        {
            // PIN is changing: remove old mappings for this StaffCode, add new pin mapping
            var keysToRemove = _staffByPin
                .Where(kvp => kvp.Value.StaffCode == staff.StaffCode)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _staffByPin.Remove(key);
            }

            _staffByPin[newPin] = staff;
        }
        else
        {
            // PIN didn't change: update Staff object in existing mappings
            var keysToUpdate = _staffByPin
                .Where(kvp => kvp.Value.StaffCode == staff.StaffCode)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToUpdate)
            {
                _staffByPin[key] = staff;
            }
        }

        return Task.CompletedTask;
    }
}
