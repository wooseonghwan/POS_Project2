namespace FishingMartPos.Services.Kicc;

/// <summary>
/// 실연동 모드에서 카드단말기 초기화가 끝나지 않았거나 실패했을 때 쓰는 게이트웨이.
/// 스텁(랜덤 승인 시뮬레이터)으로 되돌아가면 실결제가 가짜 승인으로 처리될 수 있으므로,
/// 실연동 모드에서는 연결 전/실패 시 반드시 이 클래스로 거절한다.
/// </summary>
public sealed class UnavailableVanPaymentGateway : IVanPaymentGateway
{
    public Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request) =>
        Task.FromResult(new VanApprovalResult { IsApproved = false, ResponseMessage = "카드단말기를 사용할 수 없습니다" });

    public Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request) =>
        Task.FromResult(new VanCancelResult { IsCancelled = false, ResponseMessage = "카드단말기를 사용할 수 없습니다" });
}

public sealed class UnavailableCashReceiptGateway : ICashReceiptGateway
{
    public Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request) =>
        Task.FromResult(new CashReceiptResult { IsIssued = false, ResponseMessage = "카드단말기를 사용할 수 없습니다" });

    public Task<CashReceiptCancelResult> RequestCancelAsync(CashReceiptCancelRequest request) =>
        Task.FromResult(new CashReceiptCancelResult { IsCancelled = false, ResponseMessage = "카드단말기를 사용할 수 없습니다" });
}
