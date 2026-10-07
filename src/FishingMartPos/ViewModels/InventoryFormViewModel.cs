using System.Collections.ObjectModel;
using System.IO;
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
    private const long MaxPhotoSizeBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedPhotoExtensions = { ".jpg", ".jpeg", ".png" };

    private readonly IProductRepository _productRepository;
    private readonly ICodeRepository _codeRepository;
    private readonly IPhotoPicker _photoPicker;
    private readonly IProductPhotoStorage _photoStorage;
    private readonly IDelayProvider _delay;
    private readonly INavigationService _navigation;
    private readonly InventoryViewModel _returnTo;
    private readonly string? _editingBarcode;

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();
    private string? _pickedPhotoFilePath;
    private bool _isFormattingPrice;

    [ObservableProperty] private string _posCatCd = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _barcodeInput = string.Empty;
    [ObservableProperty] private string _priceInput = string.Empty;
    [ObservableProperty] private string _stockInput = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _photoPreviewPath;
    [ObservableProperty] private string? _toastMessage;
    [ObservableProperty] private bool _isPosCatCodeManagerOpen;
    [ObservableProperty] private string _newCodeCode = string.Empty;
    [ObservableProperty] private string _newCodeName = string.Empty;

    public ObservableCollection<CodeItem> PosCatCodes { get; } = new();

    public bool IsEditMode => _editingBarcode is not null;
    public string HeaderText => IsEditMode ? "상품수정" : "상품등록";
    public string SaveButtonText => IsEditMode ? "저장하기" : "등록하기";

    public InventoryFormViewModel(
        IProductRepository productRepository,
        ICodeRepository codeRepository,
        IPhotoPicker photoPicker,
        IProductPhotoStorage photoStorage,
        IDelayProvider delay,
        INavigationService navigation,
        InventoryViewModel returnTo,
        Product? editingProduct)
    {
        _productRepository = productRepository;
        _codeRepository = codeRepository;
        _photoPicker = photoPicker;
        _photoStorage = photoStorage;
        _delay = delay;
        _navigation = navigation;
        _returnTo = returnTo;
        _editingBarcode = editingProduct?.Barcode;

        if (editingProduct is not null)
        {
            // 바코드 일괄등록 상품은 POS분류가 비어 있을(NULL) 수 있으므로 빈 문자열로 대체한다.
            PosCatCd = editingProduct.PosCatCd ?? string.Empty;
            Name = editingProduct.Name;
            BarcodeInput = editingProduct.Barcode;
            PriceInput = editingProduct.Price.ToString("N0");
            StockInput = editingProduct.StockQty.ToString();
            if (editingProduct.PhotoPath is not null)
            {
                PhotoPreviewPath = System.IO.Path.Combine(AppContext.BaseDirectory, editingProduct.PhotoPath);
            }
        }
    }

    partial void OnPriceInputChanged(string value)
    {
        if (_isFormattingPrice) return;

        string digitsOnly = new string(value.Where(char.IsDigit).ToArray());
        string formatted = digitsOnly.Length == 0 ? string.Empty : decimal.Parse(digitsOnly).ToString("N0");

        if (formatted != value)
        {
            _isFormattingPrice = true;
            PriceInput = formatted;
            _isFormattingPrice = false;
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

        await FillAsync("POSCAT", PosCatCodes);
    }

    [RelayCommand]
    private void PickPhoto()
    {
        var path = _photoPicker.PickPhoto();
        if (path is null)
        {
            return;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!AllowedPhotoExtensions.Contains(extension))
        {
            ErrorMessage = "이미지 파일(jpg, jpeg, png)만 업로드할 수 있습니다";
            return;
        }

        if (new FileInfo(path).Length > MaxPhotoSizeBytes)
        {
            ErrorMessage = "이미지 용량은 5MB 이하만 가능합니다";
            return;
        }

        ErrorMessage = null;
        _pickedPhotoFilePath = path;
        PhotoPreviewPath = path;
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
            PosCatCd = PosCatCd,
            Name = Name,
            Price = price,
            StockQty = stock,
            PhotoPath = photoPath,
        };

        await _productRepository.SaveAsync(product);

        ToastMessage = IsEditMode ? "수정되었습니다" : "저장되었습니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;

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
    private void TogglePosCatCodeManager() => IsPosCatCodeManagerOpen = !IsPosCatCodeManagerOpen;

    [RelayCommand]
    private async Task AddPosCatCode() => await AddCodeAsync("POSCAT", PosCatCodes);

    private async Task AddCodeAsync(string codeGbn, ObservableCollection<CodeItem> target)
    {
        if (string.IsNullOrWhiteSpace(NewCodeCode) || string.IsNullOrWhiteSpace(NewCodeName))
        {
            ErrorMessage = "코드와 이름을 모두 입력해주세요";
            return;
        }

        if (target.Any(c => c.Code == NewCodeCode))
        {
            ErrorMessage = "이미 존재하는 코드입니다";
            return;
        }

        await _codeRepository.AddAsync(codeGbn, NewCodeCode, NewCodeName);
        target.Add(new CodeItem { Code = NewCodeCode, Name = NewCodeName, SortNo = 0 });
        NewCodeCode = string.Empty;
        NewCodeName = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task DeletePosCatCode(CodeItem code) => await DeleteCodeAsync("POSCAT", PosCatCodes, code, p => p.PosCatCd);

    private async Task DeleteCodeAsync(string codeGbn, ObservableCollection<CodeItem> target, CodeItem code, Func<Product, string> codeSelector)
    {
        if (_allProducts.Any(p => codeSelector(p) == code.Code))
        {
            ErrorMessage = "사용 중인 코드는 삭제할 수 없습니다";
            return;
        }

        await _codeRepository.DeleteAsync(codeGbn, code.Code);
        target.Remove(code);
        ErrorMessage = null;
    }
}
