using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeVanPaymentGateway : IVanPaymentGateway
{
    private readonly VanApprovalResult _approvalResult;
    private readonly VanCancelResult _cancelResult;
    public List<VanApprovalRequest> Requests { get; } = new();
    public List<VanCancelRequest> CancelRequests { get; } = new();

    public FakeVanPaymentGateway(VanApprovalResult approvalResult, VanCancelResult? cancelResult = null)
    {
        _approvalResult = approvalResult;
        _cancelResult = cancelResult ?? new VanCancelResult { IsCancelled = true, ResponseMessage = "카드 결제 취소 완료" };
    }

    public Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(_approvalResult);
    }

    public Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request)
    {
        CancelRequests.Add(request);
        return Task.FromResult(_cancelResult);
    }
}
