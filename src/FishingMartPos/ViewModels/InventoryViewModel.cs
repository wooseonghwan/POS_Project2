using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class InventoryViewModel : ObservableObject
{
    private const string AllCategoriesCode = "";

    private readonly IProductRepository _productRepository;
    private readonly ICodeRepository _codeRepository;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _mainMenuViewModel;

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();
    private Dictionary<string, string> _majorNames = new();
    private Dictionary<string, string> _minorNames = new();
    private Dictionary<string, string> _posCatNames = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CategoryFilterOptionViewModel? _selectedCategoryOption;

    [ObservableProperty]
    private bool _isDeleteConfirmVisible;

    [ObservableProperty]
    private string? _pendingDeleteName;

    private string? _pendingDeleteBarcode;

    public ObservableCollection<CategoryFilterOptionViewModel> CategoryOptions { get; } = new();
    public ObservableCollection<InventoryRowViewModel> Rows { get; } = new();

    public bool IsAdmin => _session.CurrentStaff?.IsAdmin ?? false;

    /// <summary>App.xaml.cs(또는 테스트)에서 주입하는 InventoryFormViewModel 팩토리 — "+ 상품등록"/행별 "수정" 진입 시 사용. product가 null이면 등록 모드, 있으면 수정 모드.</summary>
    public Func<InventoryViewModel, Product?, Task<InventoryFormViewModel>>? InventoryFormViewModelFactory { get; set; }

    public InventoryViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        ICurrentSession session,
        INavigationService navigation,
        MainMenuViewModel mainMenuViewModel)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _session = session;
        _navigation = navigation;
        _mainMenuViewModel = mainMenuViewModel;
    }

    partial void OnSearchTextChanged(string value) => RefreshRows();

    partial void OnSelectedCategoryOptionChanged(CategoryFilterOptionViewModel? value) => RefreshRows();

    public async Task LoadAsync()
    {
        _allProducts = await _productRepository.GetActiveAsync();
        var majorCodes = await _codeRepository.GetByGroupAsync("MAJOR");
        var minorCodes = await _codeRepository.GetByGroupAsync("MINOR");
        var posCats = await _codeRepository.GetByGroupAsync("POSCAT");

        _majorNames = majorCodes.ToDictionary(c => c.Code, c => c.Name);
        _minorNames = minorCodes.ToDictionary(c => c.Code, c => c.Name);
        _posCatNames = posCats.ToDictionary(c => c.Code, c => c.Name);

        CategoryOptions.Clear();
        CategoryOptions.Add(new CategoryFilterOptionViewModel { Code = AllCategoriesCode, Name = "전체" });
        foreach (var cat in posCats)
        {
            CategoryOptions.Add(new CategoryFilterOptionViewModel { Code = cat.Code, Name = cat.Name });
        }

        SelectedCategoryOption = CategoryOptions[0];
    }

    private void RefreshRows()
    {
        Rows.Clear();
        bool isAdmin = IsAdmin;
        string categoryFilter = SelectedCategoryOption?.Code ?? AllCategoriesCode;
        string search = SearchText.Trim();

        int swatchIndex = 0;
        foreach (var product in _allProducts)
        {
            if (categoryFilter != AllCategoriesCode && product.PosCatCd != categoryFilter)
            {
                continue;
            }

            if (search.Length > 0 &&
                !product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !product.Barcode.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var captured = product;
            Rows.Add(new InventoryRowViewModel
            {
                Barcode = captured.Barcode,
                Name = captured.Name,
                MajorName = _majorNames.TryGetValue(captured.MajorCd, out var majorName) ? majorName : captured.MajorCd,
                MinorName = _minorNames.TryGetValue(captured.MinorCd, out var minorName) ? minorName : captured.MinorCd,
                PosCatName = _posCatNames.TryGetValue(captured.PosCatCd, out var posCatName) ? posCatName : captured.PosCatCd,
                PriceStr = CurrencyFormat.Format(captured.Price),
                StockQtyStr = captured.StockQty.ToString("N0"),
                Swatch = SwatchCycler.ForIndex(swatchIndex),
                PhotoAbsolutePath = captured.PhotoPath is not null
                    ? System.IO.Path.Combine(AppContext.BaseDirectory, captured.PhotoPath)
                    : null,
                CanDelete = isAdmin,
                CanEdit = isAdmin,
                DeleteCommand = new RelayCommand(() => RequestDelete(captured)),
                EditCommand = new AsyncRelayCommand(() => GoToEditProduct(captured)),
            });
            swatchIndex++;
        }
    }

    private void RequestDelete(Product product)
    {
        if (!IsAdmin) return;
        _pendingDeleteBarcode = product.Barcode;
        PendingDeleteName = product.Name;
        IsDeleteConfirmVisible = true;
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_mainMenuViewModel);

    [RelayCommand]
    private async Task GoToAddProduct()
    {
        if (!IsAdmin) return;
        var formVm = await InventoryFormViewModelFactory!.Invoke(this, null);
        await formVm.LoadAsync();
        _navigation.NavigateTo(formVm);
    }

    private async Task GoToEditProduct(Product product)
    {
        if (!IsAdmin) return;
        var formVm = await InventoryFormViewModelFactory!.Invoke(this, product);
        await formVm.LoadAsync();
        _navigation.NavigateTo(formVm);
    }

    [RelayCommand]
    private async Task ConfirmDelete()
    {
        if (_pendingDeleteBarcode is null) return;

        await _productRepository.DeactivateAsync(_pendingDeleteBarcode);
        _allProducts = _allProducts.Where(p => p.Barcode != _pendingDeleteBarcode).ToList();
        CancelDelete();
        RefreshRows();
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _pendingDeleteBarcode = null;
        PendingDeleteName = null;
        IsDeleteConfirmVisible = false;
    }
}
