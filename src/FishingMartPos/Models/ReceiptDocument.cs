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
    // 취소된 거래의 재발행 영수증인지. true면 인쇄/미리보기에 취소 표시를 덧붙인다(PosViewModel이 결제 직후
    // 새로 만드는 영수증은 항상 false).
    public bool IsCancelled { get; init; }
}

public sealed record ReceiptLine(string ProductName, int Qty, decimal UnitPrice, decimal LineAmt);
