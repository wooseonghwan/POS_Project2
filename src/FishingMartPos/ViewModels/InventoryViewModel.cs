using System.Collections.ObjectModel;
using System.Windows.Media;
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
    private const int PageSize = 15;

    private readonly IProductRepository _productRepository;
    private readonly ICodeRepository _codeRepository;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _mainMenuViewModel;

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();
    private IReadOnlyList<Product> _filteredProducts = Array.Empty<Product>();
    private Dictionary<string, string> _posCatNames = new();

    // POS분류가 비어 있는(NULL) 상품도 있으므로(예: 바코드 일괄등록), null을 그대로 Dictionary에 조회하면
    // ArgumentNullException이 난다. 코드가 없으면 빈 문자열로 표시한다.
    private static string LookupName(Dictionary<string, string> names, string? code)
    {
        if (code is null) return string.Empty;
        return names.TryGetValue(code, out var name) ? name : code;
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CategoryFilterOptionViewModel? _selectedCategoryOption;

    [ObservableProperty]
    private bool _isDeleteConfirmVisible;

    [ObservableProperty]
    private string? _pendingDeleteName;

    private string? _pendingDeleteBarcode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoToPreviousPage))]
    [NotifyPropertyChangedFor(nameof(CanGoToNextPage))]
    private int _currentPage = 1;

    [ObservableProperty]
    private bool _isDetailVisible;

    [ObservableProperty]
    private InventoryDetailViewModel? _detailProduct;

    public ObservableCollection<CategoryFilterOptionViewModel> CategoryOptions { get; } = new();
    public ObservableCollection<InventoryRowViewModel> Rows { get; } = new();

    public bool IsAdmin => _session.CurrentStaff?.IsAdmin ?? false;

    public int TotalPages => _filteredProducts.Count == 0
        ? 1
        : (int)Math.Ceiling(_filteredProducts.Count / (double)PageSize);

    public bool CanGoToPreviousPage => CurrentPage > 1;

    public bool CanGoToNextPage => CurrentPage < TotalPages;

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

    partial void OnSearchTextChanged(string value) => ResetToFirstPage();

    partial void OnSelectedCategoryOptionChanged(CategoryFilterOptionViewModel? value) => ResetToFirstPage();

    partial void OnCurrentPageChanged(int value) => RefreshRows();

    private void ResetToFirstPage()
    {
        if (CurrentPage != 1)
        {
            CurrentPage = 1;
        }
        else
        {
            RefreshRows();
        }
    }

    public async Task LoadAsync()
    {
        _allProducts = await _productRepository.GetActiveAsync();
        var posCats = await _codeRepository.GetByGroupAsync("POSCAT");

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
        bool isAdmin = IsAdmin;
        string categoryFilter = SelectedCategoryOption?.Code ?? AllCategoriesCode;
        string search = SearchText.Trim();

        _filteredProducts = _allProducts.Where(product =>
            (categoryFilter == AllCategoriesCode || product.PosCatCd == categoryFilter) &&
            (search.Length == 0 ||
                product.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                product.Barcode.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));

        // "전체" 보기나 검색 중에는 어느 탭 기준으로 순서를 바꾸는 건지 애매해지므로, 특정 POS분류 하나로
        // 필터링했을 때만(그리고 관리자만) 순서 변경 버튼을 쓸 수 있게 한다.
        bool canReorder = isAdmin && categoryFilter != AllCategoriesCode && search.Length == 0;

        Rows.Clear();
        int swatchIndex = (CurrentPage - 1) * PageSize;
        foreach (var product in _filteredProducts.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
        {
            var captured = product;
            var swatch = SwatchCycler.ForIndex(swatchIndex);
            Rows.Add(new InventoryRowViewModel
            {
                Barcode = captured.Barcode,
                Name = captured.Name,
                PosCatName = LookupName(_posCatNames, captured.PosCatCd),
                PriceStr = CurrencyFormat.Format(captured.Price),
                StockQtyStr = captured.StockQty.ToString("N0"),
                SortNoStr = captured.SortNo.ToString(),
                Swatch = swatch,
                PhotoAbsolutePath = captured.PhotoPath is not null
                    ? System.IO.Path.Combine(AppContext.BaseDirectory, captured.PhotoPath)
                    : null,
                CanDelete = isAdmin,
                CanEdit = isAdmin,
                CanReorder = canReorder,
                DeleteCommand = new RelayCommand(() => RequestDelete(captured)),
                EditCommand = new AsyncRelayCommand(() => GoToEditProduct(captured)),
                ShowDetailCommand = new RelayCommand(() => ShowDetail(captured, swatch)),
                MoveUpCommand = new AsyncRelayCommand(() => MoveProductAsync(captured, -1)),
                MoveDownCommand = new AsyncRelayCommand(() => MoveProductAsync(captured, 1)),
            });
            swatchIndex++;
        }
    }

    /// <summary>같은 POS분류 탭 안에서 상품을 한 칸 위/아래로 옮기고 즉시 저장한다. "전체" 보기나 검색 중에는 호출되지 않는다(CanReorder=false).</summary>
    private async Task MoveProductAsync(Product product, int direction)
    {
        if (!IsAdmin) return;
        string categoryFilter = SelectedCategoryOption?.Code ?? AllCategoriesCode;
        if (categoryFilter == AllCategoriesCode || SearchText.Trim().Length > 0) return;

        var ordered = _filteredProducts.ToList();
        int index = ordered.FindIndex(p => p.Barcode == product.Barcode);
        int newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= ordered.Count) return;

        (ordered[index], ordered[newIndex]) = (ordered[newIndex], ordered[index]);

        await _productRepository.UpdateSortOrderAsync(categoryFilter, ordered.Select(p => p.Barcode).ToList());
        _allProducts = await _productRepository.GetActiveAsync();
        RefreshRows();
    }

    private void ShowDetail(Product product, Brush swatch)
    {
        DetailProduct = new InventoryDetailViewModel
        {
            Barcode = product.Barcode,
            Name = product.Name,
            PosCatName = LookupName(_posCatNames, product.PosCatCd),
            PriceStr = CurrencyFormat.Format(product.Price),
            StockQtyStr = product.StockQty.ToString("N0"),
            Swatch = swatch,
            PhotoAbsolutePath = product.PhotoPath is not null
                ? System.IO.Path.Combine(AppContext.BaseDirectory, product.PhotoPath)
                : null,
        };
        IsDetailVisible = true;
    }

    [RelayCommand]
    private void CloseDetail()
    {
        IsDetailVisible = false;
        DetailProduct = null;
    }

    [RelayCommand]
    private void NextPage()
    {
        if (!CanGoToNextPage) return;
        CurrentPage++;
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (!CanGoToPreviousPage) return;
        CurrentPage--;
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
