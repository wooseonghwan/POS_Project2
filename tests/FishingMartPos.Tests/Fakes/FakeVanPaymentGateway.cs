using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeVanPaymentGateway : IVanPaymentGateway
{
    private readonly VanApprovalResult _result;
    public List<VanApprovalRequest> Requests { get; } = new();

    public FakeVanPaymentGateway(VanApprovalResult result)
    {
        _result = result;
    }

    public Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(_result);
    }
}
