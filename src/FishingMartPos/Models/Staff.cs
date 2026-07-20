namespace FishingMartPos.Models;

public sealed class Staff
{
    public required string StaffCode { get; init; }
    public required string StaffName { get; init; }
    public required string Role { get; init; } // "ADMIN" or "STAFF"
    public required string UseYn { get; init; }

    public bool IsAdmin => Role == "ADMIN";
}
