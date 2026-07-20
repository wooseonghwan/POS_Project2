namespace FishingMartPos.Models;

public sealed class SaleHeader
{
    public required string PosCd { get; init; }
    public required DateTime SaleDt { get; init; }
    public required string StaffCd { get; init; }
    public required decimal TotalAmt { get; init; }
    public required string PayType { get; init; } // "CASH" / "CARD1" / "CARD2"
    public decimal? CashReceived { get; init; }
    public decimal? ChangeAmt { get; init; }
    public string? VanApprovalNo { get; init; }
    public string? VanCode { get; init; }
}
