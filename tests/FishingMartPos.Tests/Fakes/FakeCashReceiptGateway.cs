using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeCashReceiptGateway : ICashReceiptGateway
{
    private readonly CashReceiptResult _result;
    public List<CashReceiptRequest> Requests { get; } = new();

    public FakeCashReceiptGateway(CashReceiptResult result)
    {
        _result = result;
    }

    public Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(_result);
    }
}
