namespace FishingMartPos.Models;

/// <summary>특정 포스단말의 특정 영업일 시작 현금(시재) 기록.</summary>
public sealed class CashDrawerEntry
{
    public required string PosCd { get; init; }
    public required DateTime BusinessDate { get; init; }
    public required decimal OpeningAmount { get; init; }
    public string? StaffCd { get; init; }
}
