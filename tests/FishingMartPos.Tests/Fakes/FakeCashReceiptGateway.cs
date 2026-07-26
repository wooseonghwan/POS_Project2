using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeCashReceiptGateway : ICashReceiptGateway
{
    private readonly CashReceiptResult _issueResult;
    private readonly CashReceiptCancelResult _cancelResult;
    public List<CashReceiptRequest> Requests { get; } = new();
    public List<CashReceiptCancelRequest> CancelRequests { get; } = new();

    public FakeCashReceiptGateway(CashReceiptResult issueResult, CashReceiptCancelResult? cancelResult = null)
    {
        _issueResult = issueResult;
        _cancelResult = cancelResult ?? new CashReceiptCancelResult { IsCancelled = true, ResponseMessage = "현금영수증 취소 완료" };
    }

    public Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(_issueResult);
    }

    public Task<CashReceiptCancelResult> RequestCancelAsync(CashReceiptCancelRequest request)
    {
        CancelRequests.Add(request);
        return Task.FromResult(_cancelResult);
    }
}
