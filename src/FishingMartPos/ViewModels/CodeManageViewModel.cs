using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;

namespace FishingMartPos.ViewModels;

public sealed partial class CodeManageViewModel : ObservableObject
{
    private readonly ICodeRepository _codeRepository;
    private readonly IProductRepository _productRepository;
    private readonly INavigationService _navigation;
    private readonly SettingsViewModel _returnTo;

    private IReadOnlyList<Product> _allProducts = Array.Empty<Product>();

    [ObservableProperty] private string _newPosCatCode = string.Empty;
    [ObservableProperty] private string _newPosCatName = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<CodeItem> PosCatCodes { get; } = new();

    public CodeManageViewModel(
        ICodeRepository codeRepository,
        IProductRepository productRepository,
        INavigationService navigation,
        SettingsViewModel returnTo)
    {
        _codeRepository = codeRepository;
        _productRepository = productRepository;
        _navigation = navigation;
        _returnTo = returnTo;
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
    private async Task AddPosCatCode() =>
        await AddCodeAsync("POSCAT", PosCatCodes, NewPosCatCode, NewPosCatName, () => { NewPosCatCode = string.Empty; NewPosCatName = string.Empty; });

    private async Task AddCodeAsync(string codeGbn, ObservableCollection<CodeItem> target, string newCode, string newName, Action clearInputs)
    {
        if (string.IsNullOrWhiteSpace(newCode) || string.IsNullOrWhiteSpace(newName))
        {
            ErrorMessage = "코드와 이름을 모두 입력해주세요";
            return;
        }

        if (target.Any(c => c.Code == newCode))
        {
            ErrorMessage = "이미 존재하는 코드입니다";
            return;
        }

        await _codeRepository.AddAsync(codeGbn, newCode, newName);
        target.Add(new CodeItem { Code = newCode, Name = newName, SortNo = 0 });
        clearInputs();
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

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
