using FishingMartPos.Services;
using FishingMartPos.Services.Kicc;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccVanPaymentGatewayTests
{
    private static readonly Dictionary<string, KiccMerchantConfig> Merchants = new()
    {
        ["CARD1"] = new KiccMerchantConfig("CARD1", "2977338", "3169055788"),
        ["CARD2"] = new KiccMerchantConfig("CARD2", "2977340", "3160326930"),
    };

    [Fact]
    public async Task RequestApprovalAsync_WhenR04IsSuccess_ReturnsApprovedWithApprovalNoAndKiccVanCode()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=99145616;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        Assert.True(result.IsApproved);
        Assert.Equal("99145616", result.ApprovalNo);
        Assert.Equal("KICC", result.VanCode);
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenR04IsNotSuccess_ReturnsDeclined()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=1234;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        Assert.False(result.IsApproved);
        Assert.Null(result.ApprovalNo);
    }

    [Fact]
    public async Task RequestApprovalAsync_WhenClientReportsFailure_ReturnsDeclinedWithFailureMessage()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Failure("응답 시간 초과"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        Assert.False(result.IsApproved);
        Assert.Equal("응답 시간 초과", result.ResponseMessage);
    }

    [Fact]
    public async Task RequestApprovalAsync_Card1_SendsDaewonSusanMerchantFields()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=1;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S03=2977338;S04=3169055788;", sendData);
    }

    [Fact]
    public async Task RequestApprovalAsync_Card2_SendsDaewonNakssiMartMerchantFields()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=1;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD2", 5000m));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S03=2977340;S04=3160326930;", sendData);
    }

    [Fact]
    public async Task RequestApprovalAsync_SendsApprovalCmdGcdJcd()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=1;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m));

        var req = Assert.Single(client.Requests);
        Assert.Equal(0xFB, req.Cmd);
        Assert.Equal(0x14, req.Gcd);
        Assert.Equal(0x04, req.Jcd);
    }

    [Fact]
    public async Task RequestApprovalAsync_WithInstallmentMonths_ForwardsToSendData()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R09=1;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestApprovalAsync(new VanApprovalRequest("1", "CARD1", 5000m, InstallmentMonths: 3));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S09=03;", sendData);
    }

    [Fact]
    public async Task RequestCancelAsync_WhenR04IsSuccess_ReturnsCancelled()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestCancelAsync(new VanCancelRequest("1", "CARD1", 5000m, 0, "99145616", "260726"));

        Assert.True(result.IsCancelled);
    }

    [Fact]
    public async Task RequestCancelAsync_WhenR04IsNotSuccess_ReturnsNotCancelled()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=1234;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        var result = await gateway.RequestCancelAsync(new VanCancelRequest("1", "CARD1", 5000m, 0, "99145616", "260726"));

        Assert.False(result.IsCancelled);
    }

    [Fact]
    public async Task RequestCancelAsync_SendsD4WithOriginalApprovalFields()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;"));
        var gateway = new KiccVanPaymentGateway(client, Merchants);

        await gateway.RequestCancelAsync(new VanCancelRequest("1", "CARD1", 5000m, 0, "99145616", "260726"));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S01=D4;", sendData);
        Assert.Contains("S12=99145616;S13=260726;", sendData);
    }
}
