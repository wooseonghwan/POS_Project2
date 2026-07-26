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
    public int InstallmentMonths { get; init; } // 0 = 일시불, 2/3/4/6/12 = 해당 개월, 그 외 양의 정수 = 기타개월
    public string CashReceiptType { get; init; } = "NONE"; // "NONE"/"PERSONAL"/"BUSINESS"
    public string? CashReceiptMerchant { get; init; } // "CARD1"/"CARD2" — 현금영수증을 어느 가맹점(van_config_tb 행)으로 등록했는지
    public string? CashReceiptApprovalNo { get; init; }
    public string? CashReceiptApprovalDate { get; init; } // YYMMDD — 향후 B2(현금영수증 취소)에 필요, 이번 범위에서는 저장만 한다
}
