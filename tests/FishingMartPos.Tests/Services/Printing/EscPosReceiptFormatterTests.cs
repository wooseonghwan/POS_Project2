using System.Text;
using FishingMartPos.Models;
using FishingMartPos.Services.Printing;
using Xunit;

namespace FishingMartPos.Tests.Services.Printing;

public class EscPosReceiptFormatterTests
{
    private static ReceiptDocument NewDocument(bool isCancelled) => new()
    {
        HeaderText = string.Empty,
        FooterText = string.Empty,
        Lines = new List<ReceiptLine> { new("지렁이", 1, 5000m, 5000m) },
        TotalAmt = 5000m,
        PayTypeLabel = "현금",
        SaleDateTime = DateTime.Now,
        SaleNo = 1,
        IsCancelled = isCancelled,
    };

    private static string Decode(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949).GetString(bytes);
    }

    [Fact]
    public void Build_CancelledReceipt_IncludesCancellationMarker()
    {
        var bytes = EscPosReceiptFormatter.Build(NewDocument(isCancelled: true));

        var text = Decode(bytes);

        Assert.Contains("취소된 거래", text);
    }

    [Fact]
    public void Build_NormalReceipt_DoesNotIncludeCancellationMarker()
    {
        var bytes = EscPosReceiptFormatter.Build(NewDocument(isCancelled: false));

        var text = Decode(bytes);

        Assert.DoesNotContain("취소된 거래", text);
    }
}
