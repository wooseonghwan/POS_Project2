using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Services.Kicc;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class PosViewModelTests
{
    private static readonly Product Bait1 = new()
    {
        Barcode = "B1", MajorCd = "FISH", MinorCd = "BAIT", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 50
    };
    private static readonly Product Float1 = new()
    {
        Barcode = "F1", MajorCd = "FISH", MinorCd = "TACKLE", PosCatCd = "FLOAT", Name = "막대찌 세트", Price = 8000, StockQty = 50
    };

    private static readonly VanApprovalResult ApprovedResult = new()
    {
        IsApproved = true,
        ApprovalNo = "20260723120000",
        VanCode = "KICC",
        ResponseMessage = "카드 결제 완료",
    };

    private static PosViewModel CreateViewModel(out FakeSalesRepository sales, out FakeHeldOrderRepository held) =>
        CreateViewModel(out sales, out held, out _, out _);

    private static PosViewModel CreateViewModel(
        out FakeSalesRepository sales,
        out FakeHeldOrderRepository held,
        out INavigationService navigation,
        out MainMenuViewModel mainMenuViewModel,
        IVanPaymentGateway? vanGateway = null,
        IKiccPosClient? kiccPosClient = null)
    {
        sales = new FakeSalesRepository();
        held = new FakeHeldOrderRepository();
        var codes = new Dictionary<string, IReadOnlyList<CodeItem>>
        {
            ["POSCAT"] = new List<CodeItem>
            {
                new() { Code = "BAIT", Name = "미끼", SortNo = 1 },
                new() { Code = "FLOAT", Name = "찌세트", SortNo = 2 },
            },
        };
        var session = new FishingMartPos.Services.CurrentSession();
        session.SignIn(
            new Staff { StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y" },
            new PosTerminal { PosCode = "1", PosName = "POS1" });
        navigation = new NavigationService();
        mainMenuViewModel = new MainMenuViewModel(session, navigation);

        return new PosViewModel(
            new FakeProductRepository(new[] { Bait1, Float1 }),
            new FakeCodeRepository(codes),
            sales,
            held,
            new FakeDelayProvider(),
            session,
            navigation,
            mainMenuViewModel,
            vanGateway ?? new FakeVanPaymentGateway(ApprovedResult),
            kiccPosClient);
    }

    [Fact]
    public async Task LoadAsync_PopulatesCategoriesAndFirstCategoryProducts()
    {
        var vm = CreateViewModel(out _, out _);

        await vm.LoadAsync();

        Assert.Equal(2, vm.Categories.Count);
        Assert.Single(vm.VisibleProducts);
        Assert.Equal("지렁이", vm.VisibleProducts[0].Name);
    }

    [Fact]
    public async Task SelectingCategory_FiltersVisibleProducts()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.Categories[1].SelectCommand.Execute(null);

        Assert.Single(vm.VisibleProducts);
        Assert.Equal("막대찌 세트", vm.VisibleProducts[0].Name);
    }

    [Fact]
    public async Task AddingProductToCart_CreatesCartLineAndUpdatesTotal()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.VisibleProducts[0].AddCommand.Execute(null);

        var line = Assert.Single(vm.CartLines);
        Assert.Equal("지렁이", line.Name);
        Assert.Equal(1, line.Qty);
        Assert.Equal("5,000원", vm.TotalAmountStr);
    }

    [Fact]
    public async Task AddingSameProductTwice_IncrementsQty()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.VisibleProducts[0].AddCommand.Execute(null);

        var line = Assert.Single(vm.CartLines);
        Assert.Equal(2, line.Qty);
    }

    [Fact]
    public async Task IncSelectedAndDecSelected_AdjustQty()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        vm.IncSelectedCommand.Execute(null);
        vm.IncSelectedCommand.Execute(null);
        vm.DecSelectedCommand.Execute(null);

        Assert.Equal(2, vm.CartLines[0].Qty);
    }

    [Fact]
    public async Task DecSelected_WhenQtyIsOne_ShowsWarningToastAndKeepsQtyAtOne()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null); // qty=1, selected
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.DecSelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.CartLines[0].Qty);
        Assert.Equal("최소 수량은 1개입니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task RemoveSelected_DeletesLine()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Empty(vm.CartLines);
    }

    [Fact]
    public async Task PressKey_WhenLineSelected_SetsExactQty()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null); // 선택 상태가 됨

        vm.PressKeyCommand.Execute("5");

        Assert.Equal(5, vm.CartLines[0].Qty);
    }

    [Fact]
    public async Task PressKey_WhenNoLineSelected_BuildsCashInput()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");

        Assert.Equal("10,000원", vm.CashInputStr);
    }

    [Fact]
    public async Task ResetOrder_ClearsCartAndCashInput()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        // No cart line is selected yet (SelectedBarcode is null), so these
        // digit presses hit the cash-input branch of PressKey rather than
        // the quantity-buffer branch.
        vm.PressKeyCommand.Execute("1");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");
        vm.PressKeyCommand.Execute("0");

        // Prove the cash input was genuinely entered before reset.
        Assert.Equal("10,000원", vm.CashInputStr);

        // Add a cart line afterwards so the post-reset "cart is cleared"
        // assertions below actually verify something (selecting this line
        // doesn't affect the already-entered CashInput).
        vm.VisibleProducts[0].AddCommand.Execute(null);

        var raisedProperties = new List<string>();
        vm.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName!);

        vm.ResetOrderCommand.Execute(null);

        Assert.Empty(vm.CartLines);
        Assert.Equal("0원", vm.TotalAmountStr);
        Assert.Equal("0원", vm.CashInputStr);
        Assert.Equal("0원", vm.ChangeStr);
        Assert.Contains("CashInputStr", raisedProperties);
        Assert.Contains("ChangeStr", raisedProperties);
    }

    [Fact]
    public async Task RequestResetOrder_WithItemsInCart_ShowsConfirmationWithoutResettingYet()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        vm.RequestResetOrderCommand.Execute(null);

        Assert.True(vm.IsResetOrderConfirmVisible);
        Assert.Single(vm.CartLines);
    }

    [Fact]
    public async Task ConfirmResetOrder_ClearsCartAndCashInput()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.RequestResetOrderCommand.Execute(null);

        vm.ConfirmResetOrderCommand.Execute(null);

        Assert.False(vm.IsResetOrderConfirmVisible);
        Assert.Empty(vm.CartLines);
        Assert.Equal("0원", vm.CashInputStr);
    }

    [Fact]
    public async Task CancelResetOrder_KeepsCartIntact()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        vm.RequestResetOrderCommand.Execute(null);

        vm.CancelResetOrderCommand.Execute(null);

        Assert.False(vm.IsResetOrderConfirmVisible);
        Assert.Single(vm.CartLines);
    }

    [Fact]
    public async Task RequestResetOrder_WhenNothingToReset_DoesNotShowConfirmation()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.RequestResetOrderCommand.Execute(null);

        Assert.False(vm.IsResetOrderConfirmVisible);
    }

    [Fact]
    public async Task GoToMainMenu_NavigatesToInjectedMainMenuViewModel()
    {
        var vm = CreateViewModel(out _, out _, out var navigation, out var mainMenuViewModel);
        await vm.LoadAsync();

        vm.GoToMainMenuCommand.Execute(null);

        Assert.Same(mainMenuViewModel, navigation.CurrentViewModel);
    }

    [Fact]
    public void CanPay_DefaultsToTrue()
    {
        var vm = CreateViewModel(out _, out _);

        Assert.True(vm.CanPay);
    }

    [Fact]
    public void SelectedInstallmentMonths_DefaultsToZero()
    {
        var vm = CreateViewModel(out _, out _);

        Assert.Equal(0, vm.SelectedInstallmentMonths);
        Assert.Equal("일시불", vm.SelectedInstallmentLabel);
        Assert.False(vm.IsCustomInstallmentSelected);
    }

    [Theory]
    [InlineData("2", 2, "2개월")]
    [InlineData("3", 3, "3개월")]
    [InlineData("12", 12, "12개월")]
    [InlineData("0", 0, "일시불")]
    public void SelectInstallment_SetsMonthsAndLabelAndClearsCustomMode(string param, int expectedMonths, string expectedLabel)
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectCustomInstallmentCommand.Execute(null); // 기타개월 먼저 선택해둔 상태에서

        vm.SelectInstallmentCommand.Execute(param);

        Assert.Equal(expectedMonths, vm.SelectedInstallmentMonths);
        Assert.Equal(expectedLabel, vm.SelectedInstallmentLabel);
        Assert.False(vm.IsCustomInstallmentSelected);
    }

    [Fact]
    public void SelectCustomInstallment_EnablesCustomModeAndResetsToZeroUntilTyped()
    {
        var vm = CreateViewModel(out _, out _);

        vm.SelectCustomInstallmentCommand.Execute(null);

        Assert.True(vm.IsCustomInstallmentSelected);
        Assert.Equal(0, vm.SelectedInstallmentMonths);
        Assert.Equal(string.Empty, vm.CustomInstallmentMonthsText);
    }

    [Fact]
    public void CustomInstallmentMonthsText_WhenCustomModeActive_ParsesIntoSelectedMonths()
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectCustomInstallmentCommand.Execute(null);

        vm.CustomInstallmentMonthsText = "8";

        Assert.Equal(8, vm.SelectedInstallmentMonths);
        Assert.Equal("8개월", vm.SelectedInstallmentLabel);
    }

    [Fact]
    public void CustomInstallmentMonthsText_WithNonPositiveOrInvalidInput_FallsBackToZero()
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectCustomInstallmentCommand.Execute(null);

        vm.CustomInstallmentMonthsText = "abc";
        Assert.Equal(0, vm.SelectedInstallmentMonths);

        vm.CustomInstallmentMonthsText = "-3";
        Assert.Equal(0, vm.SelectedInstallmentMonths);
    }

    [Fact]
    public void CustomInstallmentMonthsText_WhenCustomModeNotActive_IsIgnored()
    {
        var vm = CreateViewModel(out _, out _);
        vm.SelectInstallmentCommand.Execute("2"); // 고정 옵션 선택, 커스텀 모드 아님

        vm.CustomInstallmentMonthsText = "9"; // 코드 경로상 발생하지 않지만 방어적으로 확인

        Assert.Equal(2, vm.SelectedInstallmentMonths); // 커스텀 모드가 아니므로 반영되지 않아야 함
    }

    [Fact]
    public async Task PayCard1_WithEmptyCart_DoesNotCallGatewayOrCreateSale()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out var sales, out _, out _, out _, vanGateway);
        await vm.LoadAsync();

        await vm.PayCard1Command.ExecuteAsync(null);

        Assert.Empty(vanGateway.Requests);
        Assert.Empty(sales.CreatedSales);
    }

    [Fact]
    public async Task PayCard1_WithEmptyCart_ShowsWarningToast()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.PayCard1Command.ExecuteAsync(null);

        Assert.True(vm.IsToastWarning);
        Assert.Equal("결제할 항목이 존재하지 않습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task PayCash_WithEmptyCart_ShowsWarningToast()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.PayCashCommand.ExecuteAsync(null);

        Assert.True(vm.IsToastWarning);
        Assert.Equal("결제할 항목이 존재하지 않습니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task PayCard1_PassesPosCodePayTypeAndAmountToGateway()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null); // 5,000원

        await vm.PayCard1Command.ExecuteAsync(null);

        var request = Assert.Single(vanGateway.Requests);
        Assert.Equal("1", request.PosCode);
        Assert.Equal("CARD1", request.PayType);
        Assert.Equal(5000m, request.Amount);
    }

    [Fact]
    public async Task PayCard1_WhenApproved_CreatesSaleWithVanFieldsAndResetsCart()
    {
        var vanGateway = new FakeVanPaymentGateway(new VanApprovalResult
        {
            IsApproved = true,
            ApprovalNo = "20260723999999",
            VanCode = "KICC",
            ResponseMessage = "카드 결제 완료",
        });
        var vm = CreateViewModel(out var sales, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);

        await vm.PayCard1Command.ExecuteAsync(null);

        var (header, _) = Assert.Single(sales.CreatedSales);
        Assert.Equal("CARD1", header.PayType);
        Assert.Equal("20260723999999", header.VanApprovalNo);
        Assert.Equal("KICC", header.VanCode);
        Assert.Empty(vm.CartLines);
    }

    [Fact]
    public async Task PayCard2_WhenDeclined_DoesNotCreateSaleAndKeepsCartWithWarningToast()
    {
        var vanGateway = new FakeVanPaymentGateway(new VanApprovalResult
        {
            IsApproved = false,
            ApprovalNo = null,
            VanCode = null,
            ResponseMessage = "한도초과",
        });
        var vm = CreateViewModel(out var sales, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.PayCard2Command.ExecuteAsync(null);

        Assert.Empty(sales.CreatedSales);
        Assert.Single(vm.CartLines);
        Assert.True(vm.IsToastWarning);
        Assert.Contains("한도초과", toastValues);
    }

    [Fact]
    public async Task PayCard1_TogglesIsCardProcessingDuringPaymentAndClearsItAfterward()
    {
        var vanGateway = new FakeVanPaymentGateway(ApprovedResult);
        var vm = CreateViewModel(out _, out _, out _, out _, vanGateway);
        await vm.LoadAsync();
        vm.VisibleProducts[0].AddCommand.Execute(null);
        var processingValues = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.IsCardProcessing))
                processingValues.Add(vm.IsCardProcessing);
        };

        await vm.PayCard1Command.ExecuteAsync(null);

        Assert.Contains(true, processingValues);
        Assert.False(vm.IsCardProcessing);
    }

    [Fact]
    public async Task ScanBarcode_WithKnownBarcode_AddsProductToCart()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.ScanBarcodeCommand.Execute("B1");

        var line = Assert.Single(vm.CartLines);
        Assert.Equal("B1", line.Barcode);
        Assert.Equal(1, line.Qty);
    }

    [Fact]
    public async Task ScanBarcode_SameBarcodeTwice_IncrementsQuantity()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.ScanBarcodeCommand.Execute("B1");
        vm.ScanBarcodeCommand.Execute("B1");

        var line = Assert.Single(vm.CartLines);
        Assert.Equal(2, line.Qty);
    }

    [Fact]
    public async Task ScanBarcode_ProductFromDifferentCategoryThanCurrentlyVisible_StillAddsToCart()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync(); // defaults to first category (BAIT); F1 is FLOAT

        vm.ScanBarcodeCommand.Execute("F1");

        var line = Assert.Single(vm.CartLines);
        Assert.Equal("F1", line.Barcode);
    }

    [Fact]
    public async Task ScanBarcode_WithUnknownBarcode_ShowsWarningToastAndDoesNotAddToCart()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.ScanBarcodeCommand.ExecuteAsync("NOPE");

        Assert.Empty(vm.CartLines);
        Assert.True(vm.IsToastWarning);
        Assert.Equal("등록되지 않은 바코드입니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task ScanBarcode_WithEmptyString_DoesNothing()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();

        vm.ScanBarcodeCommand.Execute(string.Empty);

        Assert.Empty(vm.CartLines);
    }

    [Fact]
    public async Task OpenCashDrawer_WhenNoKiccClientConfigured_ShowsNotSupportedToast()
    {
        var vm = CreateViewModel(out _, out _);
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.OpenCashDrawerCommand.ExecuteAsync(null);

        Assert.True(vm.IsToastWarning);
        Assert.Equal("돈통 연동은 지원 예정입니다", Assert.Single(toastValues));
    }

    [Fact]
    public async Task OpenCashDrawer_WhenKiccClientConfigured_SendsCashDrawerCommand()
    {
        var fakeClient = new FakeKiccPosClient(KiccRawResponse.Success(""));
        var vm = CreateViewModel(out _, out _, out _, out _, kiccPosClient: fakeClient);
        await vm.LoadAsync();

        await vm.OpenCashDrawerCommand.ExecuteAsync(null);

        var req = Assert.Single(fakeClient.Requests);
        Assert.Equal(0xFB, req.Cmd);
        Assert.Equal(0x14, req.Gcd);
        Assert.Equal(0x0B, req.Jcd);
    }

    [Fact]
    public async Task OpenCashDrawer_TogglesIsCardProcessingDuringRequestAndClearsItAfterward()
    {
        var fakeClient = new FakeKiccPosClient(KiccRawResponse.Success(""));
        var vm = CreateViewModel(out _, out _, out _, out _, kiccPosClient: fakeClient);
        await vm.LoadAsync();
        var processingValues = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.IsCardProcessing))
                processingValues.Add(vm.IsCardProcessing);
        };

        await vm.OpenCashDrawerCommand.ExecuteAsync(null);

        Assert.Contains(true, processingValues);
        Assert.False(vm.IsCardProcessing);
    }

    [Fact]
    public async Task OpenCashDrawer_WhenRequestFails_ShowsFailureWarningToast()
    {
        var fakeClient = new FakeKiccPosClient(KiccRawResponse.Failure("어떤 사유"));
        var vm = CreateViewModel(out _, out _, out _, out _, kiccPosClient: fakeClient);
        await vm.LoadAsync();
        var toastValues = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PosViewModel.ToastMessage) && vm.ToastMessage is not null)
                toastValues.Add(vm.ToastMessage);
        };

        await vm.OpenCashDrawerCommand.ExecuteAsync(null);

        Assert.True(vm.IsToastWarning);
        Assert.Equal("돈통 열기에 실패했습니다", Assert.Single(toastValues));
    }
}
