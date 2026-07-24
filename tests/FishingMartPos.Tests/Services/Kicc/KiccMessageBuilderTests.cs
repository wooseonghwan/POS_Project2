using FishingMartPos.Services.Kicc;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccMessageBuilderTests
{
    [Fact]
    public void BuildApprovalRequest_ComputesVatAndFormatsFields_MatchingKiccDocSample()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 1004m, "POSTRAN123");

        Assert.Equal("S00=002;S01=D1;S02=40;S03=0788888;S04=1234567890;S09=00;S10=1004;S15=0;S16=91;S23=POSTRAN123;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_Card1_UsesDaewonSusanTidAndBusinessNo()
    {
        var card1 = new KiccMerchantConfig("CARD1", "2977338", "3169055788");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(card1, 5000m, "T1");

        Assert.Contains("S03=2977338;S04=3169055788;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_Card2_UsesDaewonNakssiMartTidAndBusinessNo()
    {
        var card2 = new KiccMerchantConfig("CARD2", "2977340", "3160326930");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(card2, 5000m, "T2");

        Assert.Contains("S03=2977340;S04=3160326930;", sendData);
    }
}
