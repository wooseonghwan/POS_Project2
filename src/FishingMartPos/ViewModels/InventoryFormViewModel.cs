using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Domain;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;

namespace FishingMartPos.ViewModels;

public sealed partial class InventoryFormViewModel : ObservableObject
{
    private readonly IProductRepository _productRepository;
    private readonly ICodeRepository _codeRepository;
    private readonly IPhotoPicker _photoPicker;
    private readonly IProductPhotoStorage _photoStorage;
    private readonly INavigationService _navigation;
    private readonly InventoryViewModel _returnTo;
    private readonly string? _editingBarcode;

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();
    private string? _pickedPhotoFilePath;

    [ObservableProperty] private string _majorCd = string.Empty;
    [ObservableProperty] private string _minorCd = string.Empty;
    [ObservableProperty] private string _posCatCd = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _barcodeInput = string.Empty;
    [ObservableProperty] private string _priceInput = string.Empty;
    [ObservableProperty] private string _stockInput = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isMajorCodeManagerOpen;
    [ObservableProperty] private bool _isMinorCodeManagerOpen;
    [ObservableProperty] private bool _isPosCatCodeManagerOpen;
    [ObservableProperty] private string _newCodeCode = string.Empty;
    [ObservableProperty] private string _newCodeName = string.Empty;

    public ObservableCollection<CodeItem> MajorCodes { get; } = new();
    public ObservableCollection<CodeItem> MinorCodes { get; } = new();
    public ObservableCollection<CodeItem> PosCatCodes { get; } = new();

    public bool IsEditMode => _editingBarcode is not null;
    public string HeaderText => IsEditMode ? "상품수정" : "상품등록";
    public string SaveButtonText => IsEditMode ? "저장하기" : "등록하기";

    public InventoryFormViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        IPhotoPicker photoPicker,
        IProductPhotoStorage photoStorage,
        INavigationService navigation,
        InventoryViewModel returnTo,
        Product? editingProduct)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _photoPicker = photoPicker;
        _photoStorage = photoStorage;
        _navigation = navigation;
        _returnTo = returnTo;
        _editingBarcode = editingProduct?.Barcode;

        if (editingProduct is not null)
        {
            MajorCd = editingProduct.MajorCd;
            MinorCd = editingProduct.MinorCd;
            PosCatCd = editingProduct.PosCatCd;
            Name = editingProduct.Name;
            BarcodeInput = editingProduct.Barcode;
            PriceInput = editingProduct.Price.ToString("0.####");
            StockInput = editingProduct.StockQty.ToString();
        }
    }

    public async Task LoadAsync()
    {
        _allProducts = await _productRepository.GetActiveAsync();

        async Task FillAsync(string codeGbn, ObservableCollection<CodeItem> target)
        {
            target.Clear();
            foreach (var code in await _codeRepository.GetByGroupAsync(codeGbn))
            {
                target.Add(code);
            }
        }

        await FillAsync("MAJOR", MajorCodes);
        await FillAsync("MINOR", MinorCodes);
        await FillAsync("POSCAT", PosCatCodes);
    }

    [RelayCommand]
    private void PickPhoto()
    {
        var path = _photoPicker.PickPhoto();
        if (path is not null)
        {
            _pickedPhotoFilePath = path;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "상품명을 입력해주세요";
            return;
        }

        if (!decimal.TryParse(PriceInput, out decimal price) || price <= 0)
        {
            ErrorMessage = "단가를 올바르게 입력해주세요";
            return;
        }

        int stock = int.TryParse(StockInput, out int parsedStock) ? parsedStock : 0;

        string barcode;
        if (string.IsNullOrWhiteSpace(BarcodeInput))
        {
            barcode = BarcodeGenerator.GenerateNext(_allProducts.Select(p => p.Barcode));
        }
        else if (!IsEditMode && _allProducts.Any(p => p.Barcode == BarcodeInput))
        {
            ErrorMessage = "이미 등록된 바코드입니다";
            return;
        }
        else
        {
            barcode = BarcodeInput;
        }

        string? photoPath = _editingBarcode is not null
            ? _allProducts.FirstOrDefault(p => p.Barcode == _editingBarcode)?.PhotoPath
            : null;
        if (_pickedPhotoFilePath is not null)
        {
            photoPath = _photoStorage.SavePhoto(barcode, _pickedPhotoFilePath);
        }

        var product = new Product
        {
            Barcode = barcode,
            MajorCd = MajorCd,
            MinorCd = MinorCd,
            PosCatCd = PosCatCd,
            Name = Name,
            Price = price,
            StockQty = stock,
            PhotoPath = photoPath,
        };

        await _productRepository.SaveAsync(product);
        await GoBackToInventoryAsync();
    }

    [RelayCommand]
    private async Task Cancel() => await GoBackToInventoryAsync();

    private async Task GoBackToInventoryAsync()
    {
        await _returnTo.LoadAsync();
        _navigation.NavigateTo(_returnTo);
    }

    [RelayCommand]
    private void ToggleMajorCodeManager() => IsMajorCodeManagerOpen = !IsMajorCodeManagerOpen;

    [RelayCommand]
    private void ToggleMinorCodeManager() => IsMinorCodeManagerOpen = !IsMinorCodeManagerOpen;

    [RelayCommand]
    private void TogglePosCatCodeManager() => IsPosCatCodeManagerOpen = !IsPosCatCodeManagerOpen;
}
