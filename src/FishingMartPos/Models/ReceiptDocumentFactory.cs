namespace FishingMartPos.Models;

public static class ReceiptDocumentFactory
{
    // ReceiptConfig 연동은 범위 밖 — 헤더/푸터는 항상 빈 문자열(PosViewModel.BuildReceiptDocument와 동일)
    public static ReceiptDocument FromSale(SaleHeader header, IReadOnlyList<SaleDetailLine> lines) => new()
    {
        HeaderText = string.Empty,
        FooterText = string.Empty,
        Lines = lines.Select(l => new ReceiptLine(l.ProductName, l.Qty, l.UnitPrice, l.LineAmt)).ToList(),
        TotalAmt = header.TotalAmt,
        PayTypeLabel = PayTypeLabel(header.PayType),
        VanApprovalNo = header.VanApprovalNo,
        InstallmentMonths = header.InstallmentMonths,
        CashReceiptTypeLabel = CashReceiptTypeLabel(header.CashReceiptType),
        CashReceiptApprovalNo = header.CashReceiptApprovalNo,
        SaleDateTime = header.SaleDt,
        SaleNo = header.SaleNo,
    };

    private static string PayTypeLabel(string payType) => payType switch
    {
        "CASH" => "현금",
        "CARD1" => "카드",
        "CARD2" => "카드",
        _ => payType,
    };

    private static string? CashReceiptTypeLabel(string type) => type switch
    {
        "PERSONAL" => "개인(소득공제)",
        "BUSINESS" => "사업자(지출증빙)",
        _ => null,
    };
}
