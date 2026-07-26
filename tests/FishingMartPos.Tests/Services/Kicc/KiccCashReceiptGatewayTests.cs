using FishingMartPos.Services;
using FishingMartPos.Services.Kicc;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccCashReceiptGatewayTests
{
    private static readonly Dictionary<string, KiccMerchantConfig> Merchants = new()
    {
        ["CARD1"] = new KiccMerchantConfig("CARD1", "2977338", "3169055788"),
        ["CARD2"] = new KiccMerchantConfig("CARD2", "2977340", "3160326930"),
    };

    [Fact]
    public async Task RequestIssueAsync_WhenR04IsSuccess_ReturnsIssuedWithApprovalNoAndDate()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R07=2507041324215;R09=149331691   ;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("1", "CARD1", "PERSONAL", 1004m));

        Assert.True(result.IsIssued);
        Assert.Equal("149331691", result.ApprovalNo);
        Assert.Equal("250704", result.ApprovalDateYyMmDd);
    }

    [Fact]
    public async Task RequestIssueAsync_WhenR04IsNotSuccess_ReturnsNotIssued()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=1234;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("1", "CARD1", "PERSONAL", 1004m));

        Assert.False(result.IsIssued);
        Assert.Null(result.ApprovalNo);
    }

    [Fact]
    public async Task RequestIssueAsync_WhenClientReportsFailure_ReturnsNotIssuedWithFailureMessage()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Failure("응답 시간 초과"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        var result = await gateway.RequestIssueAsync(new CashReceiptRequest("1", "CARD1", "PERSONAL", 1004m));

        Assert.False(result.IsIssued);
        Assert.Equal("응답 시간 초과", result.ResponseMessage);
    }

    [Fact]
    public async Task RequestIssueAsync_Card1_SendsDaewonSusanMerchantFieldsAndB1Command()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R07=2507041324215;R09=1;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        await gateway.RequestIssueAsync(new CashReceiptRequest("1", "CARD1", "BUSINESS", 5000m));

        var req = Assert.Single(client.Requests);
        Assert.Equal(0xFB, req.Cmd);
        Assert.Equal(0x14, req.Gcd);
        Assert.Equal(0x04, req.Jcd);
        Assert.Contains("S01=B1;", req.SendData);
        Assert.Contains("S03=2977338;", req.SendData);
        Assert.Contains("S11=01;", req.SendData);
    }

    [Fact]
    public async Task RequestIssueAsync_Card2_SendsDaewonNakssiMartMerchantFields()
    {
        var client = new FakeKiccPosClient(KiccRawResponse.Success("R04=0000;R07=2507041324215;R09=1;"));
        var gateway = new KiccCashReceiptGateway(client, Merchants);

        await gateway.RequestIssueAsync(new CashReceiptRequest("1", "CARD2", "PERSONAL", 5000m));

        var sendData = Assert.Single(client.Requests).SendData;
        Assert.Contains("S03=2977340;", sendData);
    }
}
