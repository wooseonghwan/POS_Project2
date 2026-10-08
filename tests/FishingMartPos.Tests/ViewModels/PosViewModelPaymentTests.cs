using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PosViewModelPaymentTests
{
    private static readonly Product Bait1 = new()
    {
        Barcode = "B1", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 50, ShowInGrid = true
    };

    private static PosViewModel CreateViewModel(
        out FakeSalesRepository sales,
        out FakeHeldOrderRepository held,
        FakeCashReceiptGateway? cashReceiptGateway = null,
        IReceiptPrinter? receiptPrinter = null)
    {
        sales = new FakeSalesRepository();
        held = new FakeHeldOrderRepository();
        var codes = new Dictionary<string, IReadOnlyList<CodeItem>>
        {
            ["POSCAT"] = new List<CodeItem> { new() { Code = "BAIT", Name = "미끼", SortNo = 1 } },
        };
        var session = new CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        var navigation = new NavigationService();
        var mainMenuViewModel = new MainMenuViewModel(session, navigation);
        var salesRepo = sales;

        return new PosViewModel(
            new FakeProductRepository(new[] { Bait1 }), new FakeCodeRepository(codes),
            sales, held, new FakeDelayProvider(), session, navigation, mainMenuViewModel,
            new FakeVanPaymentGateway(new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = "20260723120000",
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            }),
            cashReceiptGateway ?? new FakeCashReceiptGateway(new CashReceiptResult
            {
                IsIssued = true,
                ApprovalNo = "149331691",
                ApprovalDateYyMmDd = "250704",
                ResponseMessage = "현금영수증 발급 완료",
            }),
            receiptPrinter ?? new StubReceiptPrinter(),
            new FakeSignatureConverter(),
            _ => Task.FromResult(new PaymentManagementViewModel(
                salesRepo, new FakeVanPaymentGateway(new VanApprovalResult { IsApproved = true, ResponseMessage = "ok" }),
                new FakeCashReceiptGateway(new CashReceiptResult { IsIssued = true, ResponseMessage = "ok" }),
                new StubReceiptPrinter(), new FakeDelayProvider(), session, navigation, mainMenuViewModel)));
    }

    [Fact]
    public async Task PayCash_WithEmptyCart_DoesNothing()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();

        await vm.PayCashCommand.ExecuteAsync(null);

        Assert.Empty(sales.CreatedSales);
    }

    [Fact]
    public async Task PayCash_WithItemsInCart_OpensConfirmPopupWithoutCreatingSale()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        await vm.PayCashCommand.ExecuteAsync(null);

        Assert.True(vm.IsCashConfirmVisible);
        Assert.Empty(sales.CreatedSales);
    }

    [Fact]
    public async Task ConfirmCashPayment_CreatesSaleClosesPopupAndClearsCart()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("CASH", sale.Header.PayType);
        Assert.Equal(5000, sale.Header.TotalAmt);
        Assert.False(vm.IsCashConfirmVisible);
        Assert.Empty(vm.CartLines);
    }

    [Fact]
    public async Task CancelCashPayment_ClosesPopupAndKeepsCartAndCashInputUntouched()
    {
        var vm = CreateViewModel(out var sales, out _);
        await vm.LoadAsync();
        // 숫자 키패드는 장바구니 줄이 선택되어 있으면 수량 버퍼로, 선택된 줄이 없으면 받은금액 입력으로 들어간다
        // (PosViewModel.PressKey) — 상품을 담기 *전에* 눌러야 받은금액에 반영된다(AddToCart가 SelectedBarcode를 설정해버리므로).
        vm.PressKeyCommand.Execute("5");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);

        vm.CancelCashPaymentCommand.Execute(null);

        Assert.False(vm.IsCashConfirmVisible);
        Assert.Empty(sales.CreatedSales);
        Assert.Single(vm.CartLines);
        Assert.Equal("5,000원", vm.CashInputStr);
    }

    [Fact]
    public async Task HoldOrder_WithEmptyCart_ShowsWarningToast()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.HoldOrderCommand.ExecuteAsync(null);

        Assert.Empty(vm.HeldOrders);
        Assert.Equal("보류할 상품이 존재하지 않습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task DeleteHeldOrder_ShowsConfirmationThenRemovesFromHeldListWithoutTouchingCart()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        var heldItem = Assert.Single(vm.HeldOrders);
        vm.VisibleProducts[0].AddCommand.Execute(null); // unrelated cart in progress

        heldItem.DeleteCommand.Execute(null);

        Assert.True(vm.IsDeleteHeldConfirmVisible);
        Assert.Single(vm.HeldOrders);

        await vm.ConfirmDeleteHeldOrderCommand.ExecuteAsync(null);

        Assert.False(vm.IsDeleteHeldConfirmVisible);
        Assert.Empty(vm.HeldOrders);
        var untouchedLine = Assert.Single(vm.CartLines);
        Assert.Equal(1, untouchedLine.Qty);
    }

    [Fact]
    public async Task CancelDeleteHeldOrder_KeepsHeldOrderInList()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        var heldItem = Assert.Single(vm.HeldOrders);

        heldItem.DeleteCommand.Execute(null);
        vm.CancelDeleteHeldOrderCommand.Execute(null);

        Assert.False(vm.IsDeleteHeldConfirmVisible);
        Assert.Single(vm.HeldOrders);
    }

    [Fact]
    public async Task HoldOrder_ThenRecall_RestoresCartAndRemovesFromHeldList()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.IncSelectedCommand.Execute(null); // qty=2

        await vm.HoldOrderCommand.ExecuteAsync(null);

        Assert.Empty(vm.CartLines);
        var heldItem = Assert.Single(vm.HeldOrders);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)heldItem.RecallCommand).ExecuteAsync(null);

        var restored = Assert.Single(vm.CartLines);
        Assert.Equal(2, restored.Qty);
        Assert.Empty(vm.HeldOrders);
    }

    [Fact]
    public async Task Recall_WhenCartHasItems_ShowsConfirmationWithoutRecallingYet()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.IncSelectedCommand.Execute(null); // qty=2
        await vm.HoldOrderCommand.ExecuteAsync(null);
        var heldItem = Assert.Single(vm.HeldOrders);

        vm.VisibleProducts[0].AddCommand.Execute(null); // fresh cart item, qty=1

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)heldItem.RecallCommand).ExecuteAsync(null);

        Assert.True(vm.IsRecallConfirmVisible);
        Assert.Single(vm.HeldOrders);
        var untouchedLine = Assert.Single(vm.CartLines);
        Assert.Equal(1, untouchedLine.Qty);
    }

    [Fact]
    public async Task ConfirmReplaceCart_ClearsCurrentCartAndAppliesHeldOrderInstead()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.IncSelectedCommand.Execute(null); // qty=2
        await vm.HoldOrderCommand.ExecuteAsync(null);
        var heldItem = Assert.Single(vm.HeldOrders);
        vm.VisibleProducts[0].AddCommand.Execute(null); // fresh cart item, qty=1
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)heldItem.RecallCommand).ExecuteAsync(null);

        await vm.ConfirmReplaceCartCommand.ExecuteAsync(null);

        Assert.False(vm.IsRecallConfirmVisible);
        Assert.Empty(vm.HeldOrders);
        var restored = Assert.Single(vm.CartLines);
        Assert.Equal(2, restored.Qty);
    }

    [Fact]
    public async Task CancelReplaceCart_KeepsCurrentCartAndHeldOrderUntouched()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.IncSelectedCommand.Execute(null); // qty=2
        await vm.HoldOrderCommand.ExecuteAsync(null);
        var heldItem = Assert.Single(vm.HeldOrders);
        vm.VisibleProducts[0].AddCommand.Execute(null); // fresh cart item, qty=1
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)heldItem.RecallCommand).ExecuteAsync(null);

        vm.CancelReplaceCartCommand.Execute(null);

        Assert.False(vm.IsRecallConfirmVisible);
        Assert.Single(vm.HeldOrders);
        var untouchedLine = Assert.Single(vm.CartLines);
        Assert.Equal(1, untouchedLine.Qty);
    }

    [Fact]
    public async Task HoldOrder_UpToTwo_BothSucceed()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();

        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        Assert.Single(vm.HeldOrders);

        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.HeldOrders.Count);
    }

    [Fact]
    public async Task HoldOrder_WhenAlreadyTwoHeld_ShowsMessageAndKeepsCurrentCartIntact()
    {
        var vm = CreateViewModel(out _, out var held);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.HoldOrderCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.HeldOrders.Count);

        vm.VisibleProducts[0].AddCommand.Execute(null);
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };
        await vm.HoldOrderCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.HeldOrders.Count);
        var restoredCartLine = Assert.Single(vm.CartLines);
        Assert.Equal(1, restoredCartLine.Qty);
        Assert.Equal("보류는 2건만 가능합니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task ConfirmCashPayment_WithNoCashReceiptSelected_DoesNotCallGateway()
    {
        var cashReceiptGateway = new FakeCashReceiptGateway(new CashReceiptResult
        {
            IsIssued = true, ApprovalNo = "1", ApprovalDateYyMmDd = "250704", ResponseMessage = "발급 완료",
        });
        var vm = CreateViewModel(out var sales, out _, cashReceiptGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        Assert.Empty(cashReceiptGateway.Requests);
        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("NONE", sale.Header.CashReceiptType);
        Assert.Null(sale.Header.CashReceiptApprovalNo);
    }

    [Fact]
    public async Task ConfirmCashPayment_WithPersonalReceiptSelected_CallsGatewayAndSavesApprovalNo()
    {
        var cashReceiptGateway = new FakeCashReceiptGateway(new CashReceiptResult
        {
            IsIssued = true, ApprovalNo = "149331691", ApprovalDateYyMmDd = "250704", ResponseMessage = "현금영수증 발급 완료",
        });
        var vm = CreateViewModel(out var sales, out _, cashReceiptGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        vm.SelectCashReceiptTypeCommand.Execute("PERSONAL");
        vm.SelectCashReceiptMerchantCommand.Execute("CARD1");

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        var request = Assert.Single(cashReceiptGateway.Requests);
        Assert.Equal("CARD1", request.MerchantPayType);
        Assert.Equal("PERSONAL", request.ReceiptType);
        var sale = Assert.Single(sales.CreatedSales);
        Assert.Equal("PERSONAL", sale.Header.CashReceiptType);
        Assert.Equal("CARD1", sale.Header.CashReceiptMerchant);
        Assert.Equal("149331691", sale.Header.CashReceiptApprovalNo);
        Assert.Equal("250704", sale.Header.CashReceiptApprovalDate);
        Assert.False(vm.IsCashConfirmVisible);
        Assert.Equal("개인(소득공제)", vm.PreviewedReceipt!.CashReceiptTypeLabel);
        Assert.Equal("149331691", vm.PreviewedReceipt.CashReceiptApprovalNo);
    }

    [Fact]
    public async Task ConfirmCashPayment_WhenGatewayDeclines_DoesNotCreateSaleAndKeepsPopupOpen()
    {
        var cashReceiptGateway = new FakeCashReceiptGateway(new CashReceiptResult
        {
            IsIssued = false, ResponseMessage = "현금영수증 발급 실패",
        });
        var vm = CreateViewModel(out var sales, out _, cashReceiptGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        vm.SelectCashReceiptTypeCommand.Execute("BUSINESS");
        vm.SelectCashReceiptMerchantCommand.Execute("CARD2");
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        Assert.Empty(sales.CreatedSales);
        Assert.True(vm.IsCashConfirmVisible);
        Assert.Single(vm.CartLines);
        Assert.True(vm.IsToastWarning);
        Assert.Equal("현금영수증 발급 실패", Assert.Single(toastValues));
    }

    [Fact]
    public async Task ConfirmCashPayment_TogglesIsCashReceiptRequestInProgressDuringCall()
    {
        var cashReceiptGateway = new FakeCashReceiptGateway(new CashReceiptResult
        {
            IsIssued = true, ApprovalNo = "1", ApprovalDateYyMmDd = "250704", ResponseMessage = "발급 완료",
        });
        var vm = CreateViewModel(out _, out _, cashReceiptGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        vm.SelectCashReceiptTypeCommand.Execute("PERSONAL");
        vm.SelectCashReceiptMerchantCommand.Execute("CARD1");
        var progressValues = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.IsCashReceiptRequestInProgress))
                progressValues.Add(vm.IsCashReceiptRequestInProgress);
        };

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        Assert.Contains(true, progressValues);
        Assert.False(vm.IsCashReceiptRequestInProgress);
    }

    [Fact]
    public async Task ConfirmCashPayment_OnSuccess_ShowsReceiptPreviewWithCartSnapshot()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);

        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        Assert.True(vm.IsReceiptPreviewVisible);
        Assert.NotNull(vm.PreviewedReceipt);
        Assert.Equal("현금", vm.PreviewedReceipt!.PayTypeLabel);
        Assert.Equal(5000, vm.PreviewedReceipt.TotalAmt);
        var line = Assert.Single(vm.PreviewedReceipt.Lines);
        Assert.Equal("지렁이", line.ProductName);
        Assert.Equal(1, line.Qty);
    }

    [Fact]
    public async Task CloseReceiptPreview_HidesPreview()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);

        vm.CloseReceiptPreviewCommand.Execute(null);

        Assert.False(vm.IsReceiptPreviewVisible);
    }

    [Fact]
    public async Task PrintReceipt_WhenPrinterFails_ShowsErrorToastAndKeepsPreviewOpen()
    {
        var vm = CreateViewModel(out _, out _, receiptPrinter: new FakeReceiptPrinter(false));
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.PrintReceiptCommand.ExecuteAsync(null);

        Assert.Equal("영수증 인쇄에 실패했습니다", Assert.Single(toastValues));
        Assert.True(vm.IsReceiptPreviewVisible);
    }

    [Fact]
    public async Task PrintReceipt_WhenPrinterSucceeds_ClosesPreviewAutomatically()
    {
        var vm = CreateViewModel(out _, out _, receiptPrinter: new FakeReceiptPrinter(true));
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        await vm.PayCashCommand.ExecuteAsync(null);
        await vm.ConfirmCashPaymentCommand.ExecuteAsync(null);
        Assert.True(vm.IsReceiptPreviewVisible);

        await vm.PrintReceiptCommand.ExecuteAsync(null);

        Assert.False(vm.IsReceiptPreviewVisible);
    }
}
