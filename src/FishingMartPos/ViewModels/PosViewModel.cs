using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Domain;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
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
    private readonly Cart _cart = new();

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();
    private string _activeCategoryCode = string.Empty;

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
    private bool _isRecallConfirmVisible;

    [ObservableProperty]
    private bool _isDeleteHeldConfirmVisible;

    [ObservableProperty]
    private bool _isClearAllConfirmVisible;

    public ObservableCollection<CategoryTabViewModel> Categories { get; } = new();
    public ObservableCollection<ProductTileViewModel> VisibleProducts { get; } = new();
    public ObservableCollection<CartLineViewModel> CartLines { get; } = new();
    public ObservableCollection<HeldOrderSummaryViewModel> HeldOrders { get; } = new();

    public PosViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        ISalesRepository salesRepository,
        IHeldOrderRepository heldOrderRepository,
        IDelayProvider delay,
        ICurrentSession session,
        INavigationService navigation,
        MainMenuViewModel mainMenuViewModel)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _salesRepository = salesRepository;
        _heldOrderRepository = heldOrderRepository;
        _delay = delay;
        _session = session;
        _navigation = navigation;
        _mainMenuViewModel = mainMenuViewModel;
    }

    public string TotalAmountStr => CurrencyFormat.Format(_cart.Total);
    public string TotalQtyStr => _cart.TotalQty.ToString("N0");
    public string CashInputStr => CashInput.Length == 0 ? "0원" : CurrencyFormat.Format(decimal.Parse(CashInput));
    public string ChangeStr => CurrencyFormat.Format(Math.Max(0, CashAmount - _cart.Total));
    private decimal CashAmount => CashInput.Length == 0 ? 0 : decimal.Parse(CashInput);

    public async Task LoadAsync()
    {
        var codes = await _codeRepository.GetByGroupAsync("POSCAT");
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
        foreach (var product in _allProducts.Where(p => p.PosCatCd == _activeCategoryCode))
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
    private void DecSelected()
    {
        if (SelectedBarcode is null) return;
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
        if (key == "CLS")
        {
            if (_cart.Lines.Count > 0)
            {
                IsClearAllConfirmVisible = true;
                return;
            }

            CashInput = string.Empty;
            OnPropertyChanged(nameof(CashInputStr));
            OnPropertyChanged(nameof(ChangeStr));
            return;
        }

        if (SelectedBarcode is not null)
        {
            if (key == "<")
            {
                QtyBuffer = string.Empty;
                _cart.SetQty(SelectedBarcode, 1);
            }
            else if (QtyBuffer.Length < 3)
            {
                QtyBuffer += key;
                _cart.SetQty(SelectedBarcode, Math.Max(1, int.Parse(QtyBuffer)));
            }
            RefreshCartLines();
            return;
        }

        if (key == "<")
        {
            CashInput = CashInput.Length > 0 ? CashInput[..^1] : string.Empty;
        }
        else if (CashInput.Length < 9)
        {
            CashInput += key;
        }
        OnPropertyChanged(nameof(CashInputStr));
        OnPropertyChanged(nameof(ChangeStr));
    }

    [RelayCommand]
    private void ConfirmClearAll()
    {
        IsClearAllConfirmVisible = false;
        _cart.Clear();
        SelectedBarcode = null;
        QtyBuffer = string.Empty;
        RefreshCartLines();
    }

    [RelayCommand]
    private void CancelClearAll()
    {
        IsClearAllConfirmVisible = false;
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
    private async Task PayCash() => await PayAsync("CASH", "현금 결제 완료");

    [RelayCommand]
    private async Task PayCard1() => await PayAsync("CARD1", "카드 결제 완료");

    [RelayCommand]
    private async Task PayCard2() => await PayAsync("CARD2", "카드 결제 완료");

    private async Task PayAsync(string payType, string toastLabel)
    {
        if (_cart.Lines.Count == 0) return;

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
        };
        var lines = _cart.Lines
            .Select(l => new SaleDetailLine { Barcode = l.Barcode, ProductName = l.Name, Qty = l.Qty, UnitPrice = l.Price })
            .ToList();

        await _salesRepository.CreateSaleAsync(header, lines);

        IsToastWarning = false;
        ToastMessage = toastLabel;
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
        ResetOrder();
    }

    [RelayCommand]
    private async Task HoldOrder()
    {
        if (_cart.Lines.Count == 0) return;

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
