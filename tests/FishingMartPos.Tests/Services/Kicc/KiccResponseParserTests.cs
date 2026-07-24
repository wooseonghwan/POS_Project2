using FishingMartPos.Services.Kicc;
using Xunit;

namespace FishingMartPos.Tests.Services.Kicc;

public class KiccResponseParserTests
{
    // 모듈 API 문서(단말기승인연동_일반(신전문)_모듈API_250620.pdf 4페이지)의 실제 CAT 응답전문 샘플 그대로.
    private const string RealCatApprovalSample =
        "S01=I1;S02=CU;S03=0788888;S04=1168119948;S05=C ;S06=I;S07=6258-04**-****-9022;S09=00;S10=1004;S15=0;S16=91;S21=N;" +
        "R01=P;R02=Q;R03=0010;R04=0000;R05=016;R06=0000;R07=2111301456162;R09=99145616;R11= N;R12=016;R13=KB국민카드;" +
        "R14=00001220713;R15=KB국민카드;R16=d;R18=00000000;R19=TEST용;R20=301441019646;R22=Y;R23=6258-04**-****-9022;";

    [Fact]
    public void Parse_RealCatApprovalResponseSample_ExtractsApprovalFields()
    {
        var fields = KiccResponseParser.Parse(RealCatApprovalSample);

        Assert.Equal("0000", fields["R04"]);
        Assert.Equal("99145616", fields["R09"]);
        Assert.Equal("KB국민카드", fields["R13"]);
    }

    [Fact]
    public void Parse_RealCatApprovalResponseSample_AlsoExtractsEchoedRequestFields()
    {
        var fields = KiccResponseParser.Parse(RealCatApprovalSample);

        Assert.Equal("I1", fields["S01"]);
        Assert.Equal("1004", fields["S10"]);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyDictionary()
    {
        var fields = KiccResponseParser.Parse("");

        Assert.Empty(fields);
    }
}
