namespace FishingMartPos.Models;

public sealed class ReceiptDocument
{
    public required string HeaderText { get; init; }
    public required string FooterText { get; init; }
    public required IReadOnlyList<ReceiptLine> Lines { get; init; }
    public required decimal TotalAmt { get; init; }
    public required string PayTypeLabel { get; init; } // 영수증에는 "현금"/"카드"로만 표시(ReceiptDocumentFactory 참고)
    public string? VanApprovalNo { get; init; }
    public int InstallmentMonths { get; init; }
    public string? CashReceiptTypeLabel { get; init; } // "개인(소득공제)"/"사업자(지출증빙)"/null(미발행)
    public string? CashReceiptApprovalNo { get; init; }
    public DateTime? SaleDateTime { get; init; }
    public long? SaleNo { get; init; }
}

public sealed record ReceiptLine(string ProductName, int Qty, decimal UnitPrice, decimal LineAmt);
