using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Domain;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Services.Kicc;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class PosViewModel : ObservableObject
{
    private readonly IProductRepository _productRepository;
    private readonly ICodeRepository _codeRepository;
    private readonly ISalesRepository _salesRepository;
    private readonly IHeldOrderRepository _heldOrderRepository;
    private readonly IDelayProvider _delay;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _mainMenuViewModel;
    private readonly IVanPaymentGateway _vanGateway;
    private readonly ICashReceiptGateway _cashReceiptGateway;
    private readonly IReceiptPrinter _receiptPrinter;
    private readonly ISignatureConverter _signatureConverter;
    private readonly IKiccPosClient? _kiccPosClient;
    private readonly Func<MainMenuViewModel, Task<PaymentManagementViewModel>> _paymentManagementViewModelFactory;
    private readonly Cart _cart = new();

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();
    private string _activeCategoryCode = string.Empty;

    private static readonly int[] FixedInstallmentMonths = { 0, 2, 3, 4, 6, 12 };
    private const decimal InstallmentMinimumAmount = 50000m;

    // 할부 자격과 서명 필요 여부는 서로 다른 업무 규칙이라 개념적으로 분리되어 있다.
    // 현재 값이 InstallmentMinimumAmount와 우연히 같을 뿐, 하나로 합치지 말 것 — 향후 각기 다른 정책으로 바뀔 수 있다.
    private const decimal CardSignatureMinimumAmount = 50000m;

    [ObservableProperty]
    private string? _selectedBarcode;

    [ObservableProperty]
    private string _qtyBuffer = string.Empty;

    [ObservableProperty]
    private string _cashInput = string.Empty;

    [ObservableProperty]
    private string? _toastMessage;

    [ObservableProperty]
    private bool _isToastWarning;

    [ObservableProperty]
    private bool _isHeldListVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPay))]
    private bool _isCardProcessing;

    [ObservableProperty]
    private bool _isRecallConfirmVisible;

    [ObservableProperty]
    private bool _isDeleteHeldConfirmVisible;

    [ObservableProperty]
    private bool _isResetOrderConfirmVisible;

    [ObservableProperty]
    private bool _isCashConfirmVisible;

    [ObservableProperty]
    private bool _isCardPaymentVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallmentPanelVisible))]
    private bool _isCardApprovalInProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallmentPanelVisible))]
    private bool _isSignatureCaptureVisible;

    [ObservableProperty]
    private bool _isCashReceiptRequestInProgress;

    [ObservableProperty]
    private bool _isReceiptPreviewVisible;

    [ObservableProperty]
    private ReceiptDocument? _previewedReceipt;

    [ObservableProperty]
    private bool _isLastTransactionVisible;

    [ObservableProperty]
    private TransactionDetailViewModel? _lastTransactionDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedInstallmentLabel))]
    private int _selectedInstallmentMonths;

    [ObservableProperty]
    private bool _isCustomInstallmentSelected;

    [ObservableProperty]
    private string _customInstallmentMonthsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCashPayment))]
    private string _selectedCashReceiptType = "NONE";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCashPayment))]
    private string? _selectedCashReceiptMerchant;

    public ObservableCollection<CategoryTabViewModel> Categories { get; } = new();
    public ObservableCollection<ProductTileViewModel> VisibleProducts { get; } = new();
    public ObservableCollection<CartLineViewModel> CartLines { get; } = new();
    public ObservableCollection<HeldOrderSummaryViewModel> HeldOrders { get; } = new();
    public ObservableCollection<InstallmentOptionViewModel> InstallmentOptions { get; } = new();

    public PosViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        ISalesRepository salesRepository,
        IHeldOrderRepository heldOrderRepository,
        IDelayProvider delay,
        ICurrentSession session,
        INavigationService navigation,
        MainMenuViewModel mainMenuViewModel,
        IVanPaymentGateway vanGateway,
        ICashReceiptGateway cashReceiptGateway,
        IReceiptPrinter receiptPrinter,
        ISignatureConverter signatureConverter,
        Func<MainMenuViewModel, Task<PaymentManagementViewModel>> paymentManagementViewModelFactory,
        IKiccPosClient? kiccPosClient = null)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _salesRepository = salesRepository;
        _heldOrderRepository = heldOrderRepository;
        _delay = delay;
        _session = session;
        _navigation = navigation;
        _mainMenuViewModel = mainMenuViewModel;
        _vanGateway = vanGateway;
        _cashReceiptGateway = cashReceiptGateway;
        _receiptPrinter = receiptPrinter;
        _signatureConverter = signatureConverter;
        _kiccPosClient = kiccPosClient;
        _paymentManagementViewModelFactory = paymentManagementViewModelFactory;

        RefreshInstallmentOptions();
    }

    public bool CanPay => !IsCardProcessing;

    public string SelectedInstallmentLabel => SelectedInstallmentMonths <= 0 ? "일시불" : $"{SelectedInstallmentMonths}개월";
    public bool IsInstallmentEligible => _cart.Total >= InstallmentMinimumAmount;
    public bool IsInstallmentPanelVisible => !IsCardApprovalInProgress && !IsSignatureCaptureVisible;
    public bool CanConfirmCashPayment => SelectedCashReceiptType == "NONE" || SelectedCashReceiptMerchant is not null;

    public string TotalAmountStr => CurrencyFormat.Format(_cart.Total);
    public string TotalQtyStr => _cart.TotalQty.ToString("N0");
    public string CashInputStr => CashInput.Length == 0 ? "0원" : CurrencyFormat.Format(decimal.Parse(CashInput));
    public string ChangeStr => CurrencyFormat.Format(Math.Max(0, CashAmount - _cart.Total));
    private decimal CashAmount => CashInput.Length == 0 ? 0 : decimal.Parse(CashInput);

    public async Task LoadAsync()
    {
        // 노출 꺼진(UseYn=false) 분류는 판매화면 메뉴 탭에서 숨긴다. 단, 그 분류의 상품은
        // 바코드 스캔으로는 여전히 담을 수 있다(_allProducts에서 걸러내지 않음).
        var codes = (await _codeRepository.GetByGroupAsync("POSCAT")).Where(c => c.UseYn).ToList();
        _allProducts = await _productRepository.GetActiveAsync();

        Categories.Clear();
        foreach (var code in codes)
        {
            Categories.Add(new CategoryTabViewModel
            {
                Code = code.Code,
                Name = code.Name,
                IsActive = Categories.Count == 0,
                SelectCommand = new RelayCommand(() => SelectCategory(code.Code)),
            });
        }

        _activeCategoryCode = codes.Count > 0 ? codes[0].Code : string.Empty;
        RefreshVisibleProducts();
        await RefreshHeldOrdersAsync();
    }

    private void SelectCategory(string code)
    {
        _activeCategoryCode = code;
        for (int i = 0; i < Categories.Count; i++)
        {
            var tab = Categories[i];
            Categories[i] = new CategoryTabViewModel
            {
                Code = tab.Code,
                Name = tab.Name,
                IsActive = tab.Code == code,
                SelectCommand = tab.SelectCommand,
            };
        }
        RefreshVisibleProducts();
    }

    private void RefreshVisibleProducts()
    {
        VisibleProducts.Clear();
        int swatchIndex = 0;
        foreach (var product in _allProducts.Where(p => p.ShowInGrid && p.PosCatCd == _activeCategoryCode))
        {
            var captured = product;
            VisibleProducts.Add(new ProductTileViewModel
            {
                Barcode = captured.Barcode,
                Name = captured.Name,
                PriceStr = CurrencyFormat.Format(captured.Price),
                Initial = captured.Name.Length > 0 ? captured.Name[..1] : "?",
                Swatch = SwatchCycler.ForIndex(swatchIndex),
                AddCommand = new RelayCommand(() => AddToCart(captured)),
            });
            swatchIndex++;
        }
    }

    private void AddToCart(Product product)
    {
        _cart.Add(product.Barcode, product.Name, product.Price);
        SelectedBarcode = product.Barcode;
        QtyBuffer = string.Empty;
        RefreshCartLines();
    }

    [RelayCommand]
    private async Task ScanBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;

        var product = _allProducts.FirstOrDefault(p => p.Barcode == barcode);
        if (product is null)
        {
            IsToastWarning = true;
            ToastMessage = "등록되지 않은 바코드입니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        AddToCart(product);
    }

    [RelayCommand]
    private void SelectCartLine(string barcode)
    {
        SelectedBarcode = barcode;
        QtyBuffer = string.Empty;
        RefreshCartLines();
    }

    [RelayCommand]
    private void IncSelected()
    {
        if (SelectedBarcode is null) return;
        _cart.Increment(SelectedBarcode);
        RefreshCartLines();
    }

    [RelayCommand]
    private async Task DecSelected()
    {
        if (SelectedBarcode is null) return;

        var line = CartLines.FirstOrDefault(l => l.Barcode == SelectedBarcode);
        if (line is not null && line.Qty <= 1)
        {
            IsToastWarning = true;
            ToastMessage = "최소 수량은 1개입니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        _cart.Decrement(SelectedBarcode);
        RefreshCartLines();
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedBarcode is null) return;
        _cart.Remove(SelectedBarcode);
        SelectedBarcode = null;
        RefreshCartLines();
    }

    [RelayCommand]
    private void PressKey(string key)
    {
        if (SelectedBarcode is not null)
        {
            if (QtyBuffer.Length < 3)
            {
                QtyBuffer += key;
                _cart.SetQty(SelectedBarcode, Math.Max(1, int.Parse(QtyBuffer)));
            }
            RefreshCartLines();
            return;
        }

        if (CashInput.Length < 9)
        {
            CashInput += key;
        }
        OnPropertyChanged(nameof(CashInputStr));
        OnPropertyChanged(nameof(ChangeStr));
    }

    [RelayCommand]
    private void ResetOrder()
    {
        _cart.Clear();
        SelectedBarcode = null;
        QtyBuffer = string.Empty;
        CashInput = string.Empty;
        RefreshCartLines();
        OnPropertyChanged(nameof(CashInputStr));
        OnPropertyChanged(nameof(ChangeStr));
    }

    [RelayCommand]
    private void RequestResetOrder()
    {
        if (_cart.Lines.Count == 0 && CashInput.Length == 0) return;
        IsResetOrderConfirmVisible = true;
    }

    [RelayCommand]
    private void ConfirmResetOrder()
    {
        IsResetOrderConfirmVisible = false;
        ResetOrder();
    }

    [RelayCommand]
    private void CancelResetOrder()
    {
        IsResetOrderConfirmVisible = false;
    }

    [RelayCommand]
    private async Task PayCash()
    {
        if (!CanPay) return;
        if (_cart.Lines.Count == 0)
        {
            IsToastWarning = true;
            ToastMessage = "결제할 항목이 존재하지 않습니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        SelectedCashReceiptType = "NONE";
        SelectedCashReceiptMerchant = null;
        IsCashReceiptRequestInProgress = false;
        _pendingCashReceiptResult = null;
        IsCashConfirmVisible = true;
    }

    private CashReceiptResult? _pendingCashReceiptResult;

    [RelayCommand]
    private async Task ConfirmCashPayment()
    {
        if (SelectedCashReceiptType != "NONE")
        {
            if (SelectedCashReceiptMerchant is not string merchant) return;

            IsCashReceiptRequestInProgress = true;
            CashReceiptResult result;
            try
            {
                result = await _cashReceiptGateway.RequestIssueAsync(
                    new CashReceiptRequest(_session.CurrentTerminal!.PosCode, merchant, SelectedCashReceiptType, _cart.Total));
            }
            finally
            {
                IsCashReceiptRequestInProgress = false;
            }

            if (!result.IsIssued)
            {
                IsToastWarning = true;
                ToastMessage = result.ResponseMessage;
                await _delay.Delay(TimeSpan.FromMilliseconds(1200));
                ToastMessage = null;
                return;
            }

            _pendingCashReceiptResult = result;
        }

        IsCashConfirmVisible = false;
        await PayAsync("CASH", "현금 결제 완료");
    }

    [RelayCommand]
    private void CancelCashPayment()
    {
        IsCashConfirmVisible = false;
    }

    private string? _pendingCardPayType;

    [RelayCommand]
    private async Task PayCard1() => await OpenCardPaymentPopupAsync("CARD1");

    [RelayCommand]
    private async Task PayCard2() => await OpenCardPaymentPopupAsync("CARD2");

    private async Task OpenCardPaymentPopupAsync(string payType)
    {
        if (!CanPay) return;
        if (_cart.Lines.Count == 0)
        {
            IsToastWarning = true;
            ToastMessage = "결제할 항목이 존재하지 않습니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        _pendingCardPayType = payType;
        SelectedInstallmentMonths = 0;
        IsCustomInstallmentSelected = false;
        CustomInstallmentMonthsText = string.Empty;
        IsCardApprovalInProgress = false;
        IsSignatureCaptureVisible = false;
        RefreshInstallmentOptions();
        IsCardPaymentVisible = true;
    }

    [RelayCommand]
    private void CancelCardPayment()
    {
        IsCardPaymentVisible = false;
        IsSignatureCaptureVisible = false;
        _pendingCardPayType = null;
    }

    [RelayCommand]
    private async Task RequestCardApproval()
    {
        if (!CanPay) return;
        if (_pendingCardPayType is null) return;

        if (_cart.Total >= CardSignatureMinimumAmount)
        {
            IsSignatureCaptureVisible = true;
            return;
        }

        await ProceedWithCardApprovalAsync(null);
    }

    [RelayCommand]
    private async Task ConfirmSignature(byte[]? bmpBytes)
    {
        if (bmpBytes is null || bmpBytes.Length == 0)
        {
            IsToastWarning = true;
            ToastMessage = "서명을 입력해주세요";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        string? signatureHex = await _signatureConverter.ConvertToHexAsync(bmpBytes);
        if (signatureHex is null)
        {
            IsToastWarning = true;
            ToastMessage = "서명 처리에 실패했습니다. 다시 시도해주세요";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        IsSignatureCaptureVisible = false;
        await ProceedWithCardApprovalAsync(signatureHex);
    }

    [RelayCommand]
    private void CancelSignature()
    {
        IsSignatureCaptureVisible = false;
    }

    private async Task ProceedWithCardApprovalAsync(string? signatureHex)
    {
        if (!CanPay) return;
        if (_pendingCardPayType is not string payType) return;

        string capturedPayType = payType;
        int installmentMonths = SelectedInstallmentMonths;
        IsCardApprovalInProgress = true;
        IsCardProcessing = true;
        try
        {
            var result = await _vanGateway.RequestApprovalAsync(
                new VanApprovalRequest(_session.CurrentTerminal!.PosCode, capturedPayType, _cart.Total, installmentMonths, signatureHex));

            IsCardApprovalInProgress = false;
            IsCardPaymentVisible = false;

            if (result.IsApproved)
            {
                var header = new SaleHeader
                {
                    PosCd = _session.CurrentTerminal!.PosCode,
                    SaleDt = DateTime.Now,
                    StaffCd = _session.CurrentStaff!.StaffCode,
                    TotalAmt = _cart.Total,
                    PayType = capturedPayType,
                    CashReceived = null,
                    ChangeAmt = null,
                    VanApprovalNo = result.ApprovalNo,
                    VanCode = result.VanCode,
                    InstallmentMonths = installmentMonths,
                };

                await _salesRepository.CreateSaleAsync(header, BuildDetailLines());

                IsToastWarning = false;
                ToastMessage = result.ResponseMessage;
                await _delay.Delay(TimeSpan.FromMilliseconds(1200));
                ToastMessage = null;
                PreviewedReceipt = BuildReceiptDocument(
                    "카드", result.ApprovalNo, installmentMonths,
                    null, null);
                IsReceiptPreviewVisible = true;
                ResetOrder();
            }
            else
            {
                IsToastWarning = true;
                ToastMessage = result.ResponseMessage;
                await _delay.Delay(TimeSpan.FromMilliseconds(1200));
                ToastMessage = null;
            }
        }
        finally
        {
            IsCardProcessing = false;
            IsCardApprovalInProgress = false;
            IsCardPaymentVisible = false;
            IsSignatureCaptureVisible = false;
            _pendingCardPayType = null;
        }
    }

    [RelayCommand]
    private void SelectInstallment(string monthsParam)
    {
        int months = int.Parse(monthsParam);
        if (months > 0 && !IsInstallmentEligible) return;

        IsCustomInstallmentSelected = false;
        SelectedInstallmentMonths = months;
    }

    [RelayCommand]
    private void SelectCustomInstallment()
    {
        if (!IsInstallmentEligible) return;

        IsCustomInstallmentSelected = true;
        SelectedInstallmentMonths = 0;
        CustomInstallmentMonthsText = string.Empty;
    }

    partial void OnCustomInstallmentMonthsTextChanged(string value)
    {
        if (!IsCustomInstallmentSelected) return;
        SelectedInstallmentMonths = int.TryParse(value, out int months) && months is > 0 and <= 99 ? months : 0;
    }

    partial void OnSelectedInstallmentMonthsChanged(int value) => RefreshInstallmentOptions();

    partial void OnIsCustomInstallmentSelectedChanged(bool value) => RefreshInstallmentOptions();

    private void RefreshInstallmentOptions()
    {
        InstallmentOptions.Clear();
        foreach (var months in FixedInstallmentMonths)
        {
            int capturedMonths = months;
            InstallmentOptions.Add(new InstallmentOptionViewModel
            {
                Months = months,
                Label = months == 0 ? "일시불" : $"{months}개월",
                IsSelected = !IsCustomInstallmentSelected && SelectedInstallmentMonths == months,
                IsEnabled = months == 0 || IsInstallmentEligible,
                SelectCommand = new RelayCommand(() => SelectInstallment(capturedMonths.ToString())),
            });
        }
    }

    [RelayCommand]
    private void SelectCashReceiptType(string type)
    {
        SelectedCashReceiptType = type;
        if (type == "NONE")
        {
            SelectedCashReceiptMerchant = null;
        }
    }

    [RelayCommand]
    private void SelectCashReceiptMerchant(string merchant)
    {
        SelectedCashReceiptMerchant = merchant;
    }

    private List<SaleDetailLine> BuildDetailLines() =>
        _cart.Lines
            .Select(l => new SaleDetailLine { Barcode = l.Barcode, ProductName = l.Name, Qty = l.Qty, UnitPrice = l.Price })
            .ToList();

    private async Task PayAsync(string payType, string toastLabel)
    {
        if (!CanPay) return;
        if (_cart.Lines.Count == 0)
        {
            IsToastWarning = true;
            ToastMessage = "결제할 항목이 존재하지 않습니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        var cashReceiptResult = _pendingCashReceiptResult;
        var header = new SaleHeader
        {
            PosCd = _session.CurrentTerminal!.PosCode,
            SaleDt = DateTime.Now,
            StaffCd = _session.CurrentStaff!.StaffCode,
            TotalAmt = _cart.Total,
            PayType = payType,
            CashReceived = payType == "CASH" ? CashAmount : null,
            ChangeAmt = payType == "CASH" ? Math.Max(0, CashAmount - _cart.Total) : null,
            VanApprovalNo = null,
            VanCode = null,
            CashReceiptType = payType == "CASH" ? SelectedCashReceiptType : "NONE",
            CashReceiptMerchant = payType == "CASH" ? SelectedCashReceiptMerchant : null,
            CashReceiptApprovalNo = cashReceiptResult?.ApprovalNo,
            CashReceiptApprovalDate = cashReceiptResult?.ApprovalDateYyMmDd,
        };

        await _salesRepository.CreateSaleAsync(header, BuildDetailLines());

        _pendingCashReceiptResult = null;
        IsToastWarning = false;
        ToastMessage = toastLabel;
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
        PreviewedReceipt = BuildReceiptDocument(
            "현금", null, 0,
            CashReceiptTypeLabel(header.CashReceiptType), header.CashReceiptApprovalNo);
        IsReceiptPreviewVisible = true;
        ResetOrder();
    }

    [RelayCommand]
    private async Task OpenCashDrawer()
    {
        if (!CanPay) return;
        if (_kiccPosClient is null)
        {
            IsToastWarning = true;
            ToastMessage = "돈통 연동은 지원 예정입니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        IsCardProcessing = true;
        try
        {
            // 돈통 열기는 사람 입력을 기다릴 이유가 없는 즉시 응답용 명령이라, 카드승인(기본 60초)보다
            // 훨씬 짧게 3초(30 * 100ms)만 기다린다.
            var result = await _kiccPosClient.RequestAsync(0xFB, 0x14, 0x0B, "", maxPollAttempts: 30);
            if (!result.IsSuccess)
            {
                IsToastWarning = true;
                ToastMessage = "돈통 열기에 실패했습니다";
                await _delay.Delay(TimeSpan.FromMilliseconds(1200));
                ToastMessage = null;
            }
        }
        finally
        {
            IsCardProcessing = false;
        }
    }

    [RelayCommand]
    private async Task HoldOrder()
    {
        if (_cart.Lines.Count == 0)
        {
            IsToastWarning = true;
            ToastMessage = "보류할 상품이 존재하지 않습니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        if (HeldOrders.Count >= 2)
        {
            IsToastWarning = true;
            ToastMessage = "보류는 2건만 가능합니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        var lines = _cart.Lines
            .Select(l => new HeldOrderLine { Barcode = l.Barcode, ProductName = l.Name, Qty = l.Qty, UnitPrice = l.Price })
            .ToList();
        await _heldOrderRepository.HoldAsync(_session.CurrentTerminal!.PosCode, _session.CurrentStaff!.StaffCode, lines);

        ResetOrder();
        await RefreshHeldOrdersAsync();
    }

    [RelayCommand]
    private void ShowHeldList() => IsHeldListVisible = true;

    [RelayCommand]
    private void HideHeldList() => IsHeldListVisible = false;

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_mainMenuViewModel);

    [RelayCommand]
    private async Task ShowPaymentManagement()
    {
        var vm = await _paymentManagementViewModelFactory.Invoke(_mainMenuViewModel);
        _navigation.NavigateTo(vm);
    }

    private long? _pendingRecallHoldNo;

    private async Task RecallOrder(long holdNo)
    {
        if (_cart.Lines.Count > 0)
        {
            _pendingRecallHoldNo = holdNo;
            IsRecallConfirmVisible = true;
            return;
        }

        await ApplyRecallAsync(holdNo);
    }

    [RelayCommand]
    private async Task ConfirmReplaceCart()
    {
        if (_pendingRecallHoldNo is not long holdNo) return;
        _pendingRecallHoldNo = null;
        IsRecallConfirmVisible = false;

        _cart.Clear();
        SelectedBarcode = null;
        await ApplyRecallAsync(holdNo);
    }

    [RelayCommand]
    private void CancelReplaceCart()
    {
        _pendingRecallHoldNo = null;
        IsRecallConfirmVisible = false;
    }

    private async Task ApplyRecallAsync(long holdNo)
    {
        var lines = await _heldOrderRepository.GetLinesAsync(holdNo);
        foreach (var line in lines)
        {
            _cart.AddExisting(line.Barcode, line.ProductName, line.UnitPrice, line.Qty);
        }
        await _heldOrderRepository.DeleteAsync(holdNo);
        RefreshCartLines();
        await RefreshHeldOrdersAsync();
        IsHeldListVisible = false;
    }

    private long? _pendingDeleteHoldNo;

    private void RequestDeleteHeldOrder(long holdNo)
    {
        _pendingDeleteHoldNo = holdNo;
        IsDeleteHeldConfirmVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteHeldOrder()
    {
        if (_pendingDeleteHoldNo is not long holdNo) return;
        _pendingDeleteHoldNo = null;
        IsDeleteHeldConfirmVisible = false;

        await _heldOrderRepository.DeleteAsync(holdNo);
        await RefreshHeldOrdersAsync();
    }

    [RelayCommand]
    private void CancelDeleteHeldOrder()
    {
        _pendingDeleteHoldNo = null;
        IsDeleteHeldConfirmVisible = false;
    }

    private async Task RefreshHeldOrdersAsync()
    {
        var held = await _heldOrderRepository.GetHeldAsync(_session.CurrentTerminal?.PosCode ?? string.Empty);
        HeldOrders.Clear();
        foreach (var item in held)
        {
            long capturedHoldNo = item.HoldNo;
            HeldOrders.Add(new HeldOrderSummaryViewModel
            {
                HoldNo = item.HoldNo,
                HeldAtStr = item.HeldAt.ToString("HH:mm:ss"),
                TotalStr = CurrencyFormat.Format(item.Total),
                RecallCommand = new AsyncRelayCommand(() => RecallOrder(capturedHoldNo)),
                DeleteCommand = new RelayCommand(() => RequestDeleteHeldOrder(capturedHoldNo)),
            });
        }
    }

    [RelayCommand]
    private void CloseReceiptPreview() => IsReceiptPreviewVisible = false;

    [RelayCommand]
    private async Task PrintReceipt()
    {
        if (PreviewedReceipt is null) return;
        bool printed = await _receiptPrinter.PrintAsync(PreviewedReceipt);
        if (!printed)
        {
            IsToastWarning = true;
            ToastMessage = "프린터 연동은 지원 예정입니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
        }
    }

    [RelayCommand]
    private async Task ShowLastTransaction()
    {
        var header = await _salesRepository.GetLastSaleAsync(_session.CurrentTerminal!.PosCode);
        if (header is null)
        {
            IsToastWarning = true;
            ToastMessage = "최근 거래가 없습니다";
            await _delay.Delay(TimeSpan.FromMilliseconds(1200));
            ToastMessage = null;
            return;
        }

        var result = await _salesRepository.GetSaleWithLinesAsync(header.SaleNo);
        if (result is null) return;

        var detail = new TransactionDetailViewModel(
            result.Value.Header, result.Value.Lines, _session.CurrentTerminal!.PosCode,
            _salesRepository, _vanGateway, _cashReceiptGateway, _receiptPrinter, _delay);
        detail.CloseRequested += () => IsLastTransactionVisible = false;

        LastTransactionDetail = detail;
        IsLastTransactionVisible = true;
    }

    private ReceiptDocument BuildReceiptDocument(
        string payTypeLabel, string? vanApprovalNo, int installmentMonths,
        string? cashReceiptTypeLabel, string? cashReceiptApprovalNo) => new()
    {
        // ReceiptConfig 연동은 이번 범위 밖 — 헤더/푸터는 항상 빈 문자열
        HeaderText = string.Empty,
        FooterText = string.Empty,
        Lines = _cart.Lines.Select(l => new ReceiptLine(l.Name, l.Qty, l.Price, l.LineTotal)).ToList(),
        TotalAmt = _cart.Total,
        PayTypeLabel = payTypeLabel,
        VanApprovalNo = vanApprovalNo,
        InstallmentMonths = installmentMonths,
        CashReceiptTypeLabel = cashReceiptTypeLabel,
        CashReceiptApprovalNo = cashReceiptApprovalNo,
        SaleDateTime = DateTime.Now,
    };

    private static string? CashReceiptTypeLabel(string type) => type switch
    {
        "PERSONAL" => "개인(소득공제)",
        "BUSINESS" => "사업자(지출증빙)",
        _ => null,
    };

    private void RefreshCartLines()
    {
        CartLines.Clear();
        foreach (var line in _cart.Lines)
        {
            string barcode = line.Barcode;
            CartLines.Add(new CartLineViewModel
            {
                Barcode = barcode,
                Name = line.Name,
                Qty = line.Qty,
                PriceStr = CurrencyFormat.Format(line.Price),
                LineTotalStr = CurrencyFormat.Format(line.LineTotal),
                IsSelected = barcode == SelectedBarcode,
                SelectCommand = SelectCartLineCommand,
            });
        }
        OnPropertyChanged(nameof(TotalAmountStr));
        OnPropertyChanged(nameof(TotalQtyStr));
    }
}
