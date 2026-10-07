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
        // 새 코드는 항상 목록 맨 끝에 추가되므로, 표시 순서(= 목록 순서) 그대로 sort_no를 다시 매겨
        // 판매화면 메뉴 탭에서도 맨 끝에 나오도록 맞춘다.
        await _codeRepository.UpdateSortOrderAsync(codeGbn, target.Select(c => c.Code).ToList());
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

    /// <summary>판매화면 메뉴 탭 노출 순서를 위/아래로 한 칸 옮긴다. POS분류에만 의미가 있다(대/소분류는 화면에 노출되지 않음).</summary>
    [RelayCommand]
    private async Task MovePosCatCodeUp(CodeItem code)
    {
        int index = PosCatCodes.IndexOf(code);
        if (index <= 0) return;

        PosCatCodes.Move(index, index - 1);
        await _codeRepository.UpdateSortOrderAsync("POSCAT", PosCatCodes.Select(c => c.Code).ToList());
    }

    [RelayCommand]
    private async Task MovePosCatCodeDown(CodeItem code)
    {
        int index = PosCatCodes.IndexOf(code);
        if (index < 0 || index >= PosCatCodes.Count - 1) return;

        PosCatCodes.Move(index, index + 1);
        await _codeRepository.UpdateSortOrderAsync("POSCAT", PosCatCodes.Select(c => c.Code).ToList());
    }

    /// <summary>판매화면 메뉴 탭 노출을 켜거나 끈다. 꺼도 코드와 그 분류에 속한 상품은 그대로 남아있고,
    /// 바코드 스캔으로는 계속 판매할 수 있다 — 탭에만 보이지 않는다.</summary>
    [RelayCommand]
    private async Task TogglePosCatVisibility(CodeItem code)
    {
        bool newUseYn = !code.UseYn;
        await _codeRepository.SetUseYnAsync("POSCAT", code.Code, newUseYn);

        int index = PosCatCodes.IndexOf(code);
        if (index >= 0)
        {
            PosCatCodes[index] = new CodeItem { Code = code.Code, Name = code.Name, SortNo = code.SortNo, UseYn = newUseYn };
        }
    }

    [RelayCommand]
    private void GoToSettings() => _navigation.NavigateTo(_returnTo);
}
