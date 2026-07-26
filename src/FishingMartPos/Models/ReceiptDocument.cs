namespace FishingMartPos.Models;

public sealed class ReceiptDocument
{
    public required string HeaderText { get; init; }
    public required string FooterText { get; init; }
    public required IReadOnlyList<ReceiptLine> Lines { get; init; }
    public required decimal TotalAmt { get; init; }
    public required string PayTypeLabel { get; init; } // "현금"/"카드결제1"/"카드결제2"
    public string? VanApprovalNo { get; init; }
    public int InstallmentMonths { get; init; }
    public string? CashReceiptTypeLabel { get; init; } // "개인(소득공제)"/"사업자(지출증빙)"/null(미발행)
    public string? CashReceiptApprovalNo { get; init; }
}

public sealed record ReceiptLine(string ProductName, int Qty, decimal UnitPrice, decimal LineAmt);
