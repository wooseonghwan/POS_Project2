using FishingMartPos.Models;
using Xunit;

namespace FishingMartPos.Tests.Models;

public class ReceiptDocumentFactoryTests
{
    [Fact]
    public void FromSale_CardSale_MapsPayTypeLabelAndApprovalNo()
    {
        var header = new SaleHeader
        {
            SaleNo = 1,
            PosCd = "1",
            SaleDt = DateTime.Now,
            StaffCd = "ADMIN1",
            TotalAmt = 22500m,
            PayType = "CARD1",
            VanApprovalNo = "99145616",
            InstallmentMonths = 3,
        };
        var lines = new List<SaleDetailLine>
        {
            new() { Barcode = "A1", ProductName = "지렁이", Qty = 1, UnitPrice = 5000m },
        };

        var document = ReceiptDocumentFactory.FromSale(header, lines);

        Assert.Equal("카드결제1", document.PayTypeLabel);
        Assert.Equal("99145616", document.VanApprovalNo);
        Assert.Equal(3, document.InstallmentMonths);
        Assert.Equal(22500m, document.TotalAmt);
        Assert.Single(document.Lines);
        Assert.Equal("지렁이", document.Lines[0].ProductName);
    }

    [Fact]
    public void FromSale_CashSaleWithReceipt_MapsCashReceiptTypeLabel()
    {
        var header = new SaleHeader
        {
            SaleNo = 2,
            PosCd = "1",
            SaleDt = DateTime.Now,
            StaffCd = "ADMIN1",
            TotalAmt = 5000m,
            PayType = "CASH",
            CashReceiptType = "PERSONAL",
            CashReceiptApprovalNo = "149331691",
        };

        var document = ReceiptDocumentFactory.FromSale(header, new List<SaleDetailLine>());

        Assert.Equal("현금", document.PayTypeLabel);
        Assert.Equal("개인(소득공제)", document.CashReceiptTypeLabel);
        Assert.Equal("149331691", document.CashReceiptApprovalNo);
    }

    [Fact]
    public void FromSale_CashSaleWithoutReceipt_CashReceiptTypeLabelIsNull()
    {
        var header = new SaleHeader
        {
            SaleNo = 3,
            PosCd = "1",
            SaleDt = DateTime.Now,
            StaffCd = "ADMIN1",
            TotalAmt = 5000m,
            PayType = "CASH",
        };

        var document = ReceiptDocumentFactory.FromSale(header, new List<SaleDetailLine>());

        Assert.Null(document.CashReceiptTypeLabel);
    }
}
