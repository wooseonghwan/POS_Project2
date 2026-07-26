using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class StubCashReceiptGatewayTests
{
    [Fact]
    public async Task RequestIssueAsync_WhenOutcomeProviderApproves_ReturnsIssuedWithApprovalNo()
    {
        var outcome = new FakeVanOutcomeProvider(isApproved: true);
        var gateway = new StubCashReceiptGateway(new FakeDelayProvider(), outcome);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("1", "CARD1", "PERSONAL", 5000m));

        Assert.True(result.IsIssued);
        Assert.NotNull(result.ApprovalNo);
        Assert.NotNull(result.ApprovalDateYyMmDd);
    }

    [Fact]
    public async Task RequestIssueAsync_WhenOutcomeProviderDeclines_ReturnsNotIssuedWithNullApprovalNo()
    {
        var outcome = new FakeVanOutcomeProvider(isApproved: false);
        var gateway = new StubCashReceiptGateway(new FakeDelayProvider(), outcome);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("1", "CARD1", "BUSINESS", 5000m));

        Assert.False(result.IsIssued);
        Assert.Null(result.ApprovalNo);
        Assert.False(string.IsNullOrEmpty(result.ResponseMessage));
    }

    [Fact]
    public async Task RequestCancelAsync_WhenOutcomeProviderApproves_ReturnsCancelled()
    {
        var gateway = new StubCashReceiptGateway(new FakeDelayProvider(), new FakeVanOutcomeProvider(isApproved: true));

        var result = await gateway.RequestCancelAsync(new CashReceiptCancelRequest("1", "CARD1", "PERSONAL", 1004m, "149331691", "250704"));

        Assert.True(result.IsCancelled);
    }
}
