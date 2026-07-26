using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class StubVanPaymentGatewayTests
{
    private static readonly string[] KnownDeclineMessages =
    {
        "한도초과", "카드 조회 실패", "가맹점 정보 오류", "응답 시간 초과",
    };

    [Fact]
    public async Task RequestApprovalAsync_WhenOutcomeIsApproved_ReturnsApprovalNoAndKiccVanCode()
    {
        var gateway = new StubVanPaymentGateway(new FakeDelayProvider(), new FakeVanOutcomeProvider(isApproved: true));

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 10000m));

        Assert.True(result.IsApproved);
        Assert.NotNull(result.ApprovalNo);
        Assert.Matches("^\\d{14}$", result.ApprovalNo!);
        Assert.Equal("KICC", result.VanCode);
        Assert.Equal("카드 결제 완료", result.ResponseMessage);
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenOutcomeIsDeclined_ReturnsNullApprovalFieldsAndKnownMessage()
    {
        var gateway = new StubVanPaymentGateway(new FakeDelayProvider(), new FakeVanOutcomeProvider(isApproved: false));

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD2", 10000m));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovalNo);
        Assert.Null(result.VanCode);
        Assert.Contains(result.ResponseMessage, KnownDeclineMessages);
    }

    [Fact]
    public async Task RequestCancelAsync_AlwaysReturnsCancelled()
    {
        var gateway = new StubVanPaymentGateway(new FakeDelayProvider(), new FakeVanOutcomeProvider(isApproved: true));

        var result = await gateway.RequestCancelAsync(new VanCancelRequest("1", "CARD1", 5000m, 0, "99145616", "260726"));

        Assert.True(result.IsCancelled);
    }
}
