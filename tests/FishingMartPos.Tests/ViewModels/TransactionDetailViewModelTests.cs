using FishingMartPos.Models;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class TransactionDetailViewModelTests
{
    private static SaleHeader CardHeader(string status = "COMPLETE") => new()
    {
        SaleNo = 10,
        PosCd = "1",
        SaleDt = new DateTime(2026, 7, 26),
        StaffCd = "ADMIN1",
        TotalAmt = 22500m,
        PayType = "CARD1",
        VanApprovalNo = "99145616",
        InstallmentMonths = 0,
        Status = status,
    };

    private static SaleHeader CashHeaderNoReceipt(string status = "COMPLETE") => new()
    {
        SaleNo = 11,
        PosCd = "1",
        SaleDt = new DateTime(2026, 7, 26),
        StaffCd = "ADMIN1",
        TotalAmt = 5000m,
        PayType = "CASH",
        CashReceiptType = "NONE",
        Status = status,
    };

    private static SaleHeader CashHeaderWithReceipt(string status = "COMPLETE") => new()
    {
        SaleNo = 12,
        PosCd = "1",
        SaleDt = new DateTime(2026, 7, 26),
        StaffCd = "ADMIN1",
        TotalAmt = 7000m,
        PayType = "CASH",
        CashReceiptType = "PERSONAL",
        CashReceiptMerchant = "CARD1",
        CashReceiptApprovalNo = "APR9",
        CashReceiptApprovalDate = "260101",
        Status = status,
    };

    private static TransactionDetailViewModel CreateViewModel(
        SaleHeader header,
        out FakeSalesRepository sales,
        out FakeVanPaymentGateway van,
        out FakeCashReceiptGateway cashReceipt,
        IReceiptPrinter? receiptPrinter = null)
    {
        sales = new FakeSalesRepository();
        van = new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" });
        cashReceipt = new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ApprovalNo = "APR1", ApprovalDateYyMmDd = "260726", ResponseMessage = "발급완료" });
        return new TransactionDetailViewModel(
            header, new List<SaleDetailLine>(), "1", sales, van, cashReceipt,
            receiptPrinter ?? new FakeReceiptPrinter(true), new FakeDelayProvider());
    }

    [Fact]
    public void CanCancel_WhenStatusIsComplete_IsTrue()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _);
        Assert.True(vm.CanCancel);
    }

    [Fact]
    public void CanCancel_WhenAlreadyCancelled_IsFalse()
    {
        var vm = CreateViewModel(CardHeader("CANCELLED"), out _, out _, out _);
        Assert.False(vm.CanCancel);
    }

    [Fact]
    public void CanConvertToCashReceipt_ForCashSaleWithoutReceipt_IsTrue()
    {
        var vm = CreateViewModel(CashHeaderNoReceipt(), out _, out _, out _);
        Assert.True(vm.CanConvertToCashReceipt);
    }

    [Fact]
    public void CanConvertToCashReceipt_ForCardSale_IsFalse()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _);
        Assert.False(vm.CanConvertToCashReceipt);
    }

    [Fact]
    public async Task ConfirmCancelCommand_ForCardSale_CallsVanCancelWithOriginalApprovalFields()
    {
        var vm = CreateViewModel(CardHeader(), out var sales, out var van, out _);

        await vm.ConfirmCancelCommand.ExecuteAsync(null);

        var request = Assert.Single(van.CancelRequests);
        Assert.Equal("99145616", request.OriginalApprovalNo);
        Assert.Equal("260726", request.OriginalApprovalDateYyMmDd);
        Assert.Single(sales.CancelledSaleNos, 10L);
        Assert.False(vm.CanCancel);
    }

    [Fact]
    public async Task ConfirmCancelCommand_ForCashSaleWithoutReceipt_SkipsGatewayCallButCancelsSale()
    {
        var vm = CreateViewModel(CashHeaderNoReceipt(), out var sales, out _, out var cashReceipt);

        await vm.ConfirmCancelCommand.ExecuteAsync(null);

        Assert.Empty(cashReceipt.CancelRequests);
        Assert.Single(sales.CancelledSaleNos, 11L);
    }

    [Fact]
    public async Task ConfirmCancelCommand_ForCashSaleWithReceipt_CallsCashReceiptCancelWithOriginalApprovalFieldsAndSkipsVan()
    {
        var vm = CreateViewModel(CashHeaderWithReceipt(), out var sales, out var van, out var cashReceipt);

        await vm.ConfirmCancelCommand.ExecuteAsync(null);

        var request = Assert.Single(cashReceipt.CancelRequests);
        Assert.Equal("CARD1", request.MerchantPayType);
        Assert.Equal("PERSONAL", request.ReceiptType);
        Assert.Equal(7000m, request.Amount);
        Assert.Equal("APR9", request.OriginalApprovalNo);
        Assert.Equal("260101", request.OriginalApprovalDateYyMmDd);
        Assert.Single(sales.CancelledSaleNos, 12L);
        Assert.Empty(van.CancelRequests);
    }

    [Fact]
    public async Task ConfirmCancelCommand_WhenAlreadyCancelled_DoesNotCallAnyGatewayOrRepository()
    {
        var vm = CreateViewModel(CardHeader("CANCELLED"), out var sales, out var van, out var cashReceipt);

        await vm.ConfirmCancelCommand.ExecuteAsync(null);

        Assert.Empty(van.CancelRequests);
        Assert.Empty(cashReceipt.CancelRequests);
        Assert.Empty(sales.CancelledSaleNos);
    }

    [Fact]
    public async Task ConfirmCancelCommand_WhenRepositoryCancelFails_SetsStatusErrorAndDoesNotThrow()
    {
        var vm = CreateViewModel(CardHeader(), out var sales, out var van, out _);
        sales.ThrowOnCancelSale = true;

        var exception = await Record.ExceptionAsync(() => vm.ConfirmCancelCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Single(van.CancelRequests);
        Assert.Empty(sales.CancelledSaleNos);
        Assert.True(vm.IsStatusError);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public async Task ConfirmReceiptConversionCommand_IssuesReceiptAndUpdatesSale()
    {
        var vm = CreateViewModel(CashHeaderNoReceipt(), out var sales, out _, out var cashReceipt);

        vm.ShowReceiptConversionCommand.Execute(null);
        vm.SelectReceiptTypeCommand.Execute("PERSONAL");
        vm.SelectReceiptMerchantCommand.Execute("CARD1");
        await vm.ConfirmReceiptConversionCommand.ExecuteAsync(null);

        Assert.Single(cashReceipt.Requests);
        var update = Assert.Single(sales.CashReceiptUpdates);
        Assert.Equal(11, update.SaleNo);
        Assert.Equal("PERSONAL", update.ReceiptType);
        Assert.False(vm.CanConvertToCashReceipt);
    }

    [Fact]
    public async Task ConfirmReceiptConversionCommand_WhenRepositoryUpdateFails_SetsStatusErrorAndDoesNotThrow()
    {
        var vm = CreateViewModel(CashHeaderNoReceipt(), out var sales, out _, out var cashReceipt);
        sales.ThrowOnUpdateCashReceipt = true;

        vm.ShowReceiptConversionCommand.Execute(null);
        vm.SelectReceiptTypeCommand.Execute("PERSONAL");
        vm.SelectReceiptMerchantCommand.Execute("CARD1");
        var exception = await Record.ExceptionAsync(() => vm.ConfirmReceiptConversionCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Single(cashReceipt.Requests);
        Assert.Empty(sales.CashReceiptUpdates);
        Assert.True(vm.IsStatusError);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
        Assert.Equal("NONE", vm.Header.CashReceiptType);
        Assert.False(vm.IsConvertingToReceipt);
    }

    [Fact]
    public void CloseCommand_InvokesCloseRequestedEvent()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _);
        bool closed = false;
        vm.CloseRequested += () => closed = true;

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public void RequestCancelCommand_WhenCancellable_ShowsConfirmOverlay()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _);

        vm.RequestCancelCommand.Execute(null);

        Assert.True(vm.IsCancelConfirmVisible);
    }

    [Fact]
    public void RequestCancelCommand_WhenAlreadyCancelled_DoesNotShowConfirmOverlay()
    {
        var vm = CreateViewModel(CardHeader("CANCELLED"), out _, out _, out _);

        vm.RequestCancelCommand.Execute(null);

        Assert.False(vm.IsCancelConfirmVisible);
    }

    [Fact]
    public void CancelCancelCommand_HidesConfirmOverlayWithoutActing()
    {
        var vm = CreateViewModel(CardHeader(), out var sales, out var van, out _);
        vm.RequestCancelCommand.Execute(null);

        vm.CancelCancelCommand.Execute(null);

        Assert.False(vm.IsCancelConfirmVisible);
        Assert.Empty(van.CancelRequests);
        Assert.Empty(sales.CancelledSaleNos);
    }

    [Fact]
    public async Task ConfirmCancelCommand_RetryAfterDbFailure_DoesNotCallGatewayAgainAndSucceedsOnRetry()
    {
        var vm = CreateViewModel(CardHeader(), out var sales, out var van, out _);
        sales.ThrowOnCancelSale = true;

        await vm.ConfirmCancelCommand.ExecuteAsync(null);

        Assert.Single(van.CancelRequests);
        Assert.Empty(sales.CancelledSaleNos);
        Assert.True(vm.IsStatusError);
        Assert.True(vm.CanCancel);

        sales.ThrowOnCancelSale = false;
        await vm.ConfirmCancelCommand.ExecuteAsync(null);

        // 게이트웨이 취소는 재시도 없이 여전히 1회만 호출되어야 한다
        Assert.Single(van.CancelRequests);
        Assert.Single(sales.CancelledSaleNos, 10L);
        Assert.Equal("CANCELLED", vm.Header.Status);
        Assert.False(vm.IsStatusError);
    }

    [Fact]
    public async Task ConfirmCancelCommand_ForCardSale_WhenVanApprovalNoMissing_SetsStatusErrorAndDoesNotCallGateway()
    {
        var header = CardHeader() with { VanApprovalNo = null };
        var vm = CreateViewModel(header, out var sales, out var van, out _);

        await vm.ConfirmCancelCommand.ExecuteAsync(null);

        Assert.Empty(van.CancelRequests);
        Assert.Empty(sales.CancelledSaleNos);
        Assert.True(vm.IsStatusError);
        Assert.Equal("원거래 승인정보가 없어 취소할 수 없습니다", vm.StatusMessage);
    }

    [Fact]
    public async Task ConfirmCancelCommand_ForCashSaleWithReceipt_WhenApprovalDateMissing_SetsStatusErrorAndDoesNotCallGateway()
    {
        var header = CashHeaderWithReceipt() with { CashReceiptApprovalDate = null };
        var vm = CreateViewModel(header, out var sales, out _, out var cashReceipt);

        await vm.ConfirmCancelCommand.ExecuteAsync(null);

        Assert.Empty(cashReceipt.CancelRequests);
        Assert.Empty(sales.CancelledSaleNos);
        Assert.True(vm.IsStatusError);
        Assert.Equal("원거래 승인정보가 없어 취소할 수 없습니다", vm.StatusMessage);
    }

    [Fact]
    public void ReissueReceiptCommand_ShowsPreviewWithDocumentMatchingSale()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _);

        vm.ReissueReceiptCommand.Execute(null);

        Assert.True(vm.IsReceiptPreviewVisible);
        Assert.NotNull(vm.PreviewedReceipt);
        Assert.Equal(22500m, vm.PreviewedReceipt!.TotalAmt);
        Assert.Equal("카드", vm.PreviewedReceipt.PayTypeLabel);
        Assert.Equal("99145616", vm.PreviewedReceipt.VanApprovalNo);
    }

    [Fact]
    public void CloseReceiptPreviewCommand_HidesPreview()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _);
        vm.ReissueReceiptCommand.Execute(null);

        vm.CloseReceiptPreviewCommand.Execute(null);

        Assert.False(vm.IsReceiptPreviewVisible);
    }

    [Fact]
    public async Task PrintReceiptCommand_WhenPrinterSucceeds_ClosesPreviewAutomatically()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _, new FakeReceiptPrinter(true));
        vm.ReissueReceiptCommand.Execute(null);
        Assert.True(vm.IsReceiptPreviewVisible);

        await vm.PrintReceiptCommand.ExecuteAsync(null);

        Assert.False(vm.IsReceiptPreviewVisible);
    }

    [Fact]
    public async Task PrintReceiptCommand_WhenPrinterFails_ShowsErrorAndKeepsPreviewOpen()
    {
        var vm = CreateViewModel(CardHeader(), out _, out _, out _, new FakeReceiptPrinter(false));
        vm.ReissueReceiptCommand.Execute(null);

        await vm.PrintReceiptCommand.ExecuteAsync(null);

        Assert.True(vm.IsReceiptPreviewVisible);
        Assert.True(vm.IsStatusError);
        Assert.Equal("영수증 인쇄에 실패했습니다", vm.StatusMessage);
    }
}
