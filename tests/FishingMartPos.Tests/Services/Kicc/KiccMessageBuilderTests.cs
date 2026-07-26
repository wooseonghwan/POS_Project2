using FishingMartPos.Services.Kicc;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccMessageBuilderTests
{
    [Fact]
    public void BuildApprovalRequest_ComputesVatAndFormatsFields_MatchingKiccDocSample()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 1004m, "POSTRAN123", installmentMonths: 0);

        Assert.Equal("S00=002;S01=D1;S02=40;S03=0788888;S04=1234567890;S09=00;S10=1004;S15=0;S16=91;S23=POSTRAN123;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_Card1_UsesDaewonSusanTidAndBusinessNo()
    {
        var card1 = new KiccMerchantConfig("CARD1", "2977338", "3169055788");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(card1, 5000m, "T1", installmentMonths: 0);

        Assert.Contains("S03=2977338;S04=3169055788;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_Card2_UsesDaewonNakssiMartTidAndBusinessNo()
    {
        var card2 = new KiccMerchantConfig("CARD2", "2977340", "3160326930");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(card2, 5000m, "T2", installmentMonths: 0);

        Assert.Contains("S03=2977340;S04=3160326930;", sendData);
    }

    [Theory]
    [InlineData(0, "S09=00;")]
    [InlineData(2, "S09=02;")]
    [InlineData(3, "S09=03;")]
    [InlineData(4, "S09=04;")]
    [InlineData(6, "S09=06;")]
    [InlineData(12, "S09=12;")]
    public void BuildApprovalRequest_InstallmentMonths_FormatsS09AsTwoDigitString(int installmentMonths, string expectedFragment)
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 5000m, "T1", installmentMonths);

        Assert.Contains(expectedFragment, sendData);
    }

    [Fact]
    public void BuildCashReceiptIssueRequest_Personal_MatchesKiccDocSample()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildCashReceiptIssueRequest(merchant, 1004m, "PERSONAL", "20250704132431000000");

        Assert.Equal("S00=002;S01=B1;S02=40;S03=0788888;S09=00;S10=1004;S11=00;S15=0;S16=91;S23=20250704132431000000;", sendData);
    }

    [Fact]
    public void BuildCashReceiptIssueRequest_Business_UsesS11Code01()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildCashReceiptIssueRequest(merchant, 1004m, "BUSINESS", "20250704132431000000");

        Assert.Contains("S11=01;", sendData);
    }

    [Fact]
    public void BuildCashReceiptCancelRequest_MatchesKiccDocSample()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildCashReceiptCancelRequest(merchant, 1004m, "PERSONAL", "149331691", "250704", "20250704132431000000");

        Assert.Equal("S00=002;S01=B2;S02=40;S03=0788888;S09=00;S10=1004;S11=00;S12=149331691;S13=250704;S15=0;S16=91;S23=20250704132431000000;", sendData);
    }

    [Fact]
    public void BuildCardCancelRequest_UsesD4CommandCode()
    {
        var merchant = new KiccMerchantConfig("CARD1", "2977338", "3169055788");

        var sendData = KiccMessageBuilder.BuildCardCancelRequest(merchant, 5000m, 0, "99145616", "260726", "1260726120000012");

        Assert.Contains("S01=D4;", sendData);
        Assert.Contains("S03=2977338;S04=3169055788;", sendData);
        Assert.Contains("S12=99145616;", sendData);
        Assert.Contains("S13=260726;", sendData);
    }

    [Fact]
    public void BuildCardCancelRequest_WithInstallmentMonths_FormatsAsTwoDigits()
    {
        var merchant = new KiccMerchantConfig("CARD1", "2977338", "3169055788");

        var sendData = KiccMessageBuilder.BuildCardCancelRequest(merchant, 60000m, 3, "99145616", "260726", "1260726120000012");

        Assert.Contains("S09=03;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_WithSignatureHex_AppendsS30AndS31Fields()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 60000m, "POSTRAN123", installmentMonths: 0, signatureHex: "AB12CD");

        Assert.EndsWith("S23=POSTRAN123;S30=1;S31=AB12CD;", sendData);
    }

    [Fact]
    public void BuildApprovalRequest_WithoutSignatureHex_OmitsS30AndS31Fields()
    {
        var merchant = new KiccMerchantConfig("CARD1", "0788888", "1234567890");

        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, 1004m, "POSTRAN123", installmentMonths: 0);

        Assert.Equal("S00=002;S01=D1;S02=40;S03=0788888;S04=1234567890;S09=00;S10=1004;S15=0;S16=91;S23=POSTRAN123;", sendData);
        Assert.DoesNotContain("S30", sendData);
        Assert.DoesNotContain("S31", sendData);
    }
}
