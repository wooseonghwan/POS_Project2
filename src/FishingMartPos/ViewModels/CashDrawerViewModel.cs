using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

/// <summary>영업일 시작 현금(시재) 입력 화면. 하루에 한 번, 포스단말별로 시작할 때 넣어두는 금액.</summary>
public sealed partial class CashDrawerViewModel : ObservableObject
{
    private readonly ICashDrawerRepository _cashDrawerRepository;
    private readonly ICurrentSession _session;
    private readonly IDelayProvider _delay;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;

    private bool _isFormattingAmount;

    [ObservableProperty] private string _businessDateStr = string.Empty;
    [ObservableProperty] private string _amountInput = string.Empty;
    // 오늘 이미 입력해 둔 금액(없으면 null). 입력란과 별개로, 현재 저장돼 있는 값을 보여주는 용도.
    [ObservableProperty] private string? _savedAmountStr;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _toastMessage;

    public CashDrawerViewModel(
        ICashDrawerRepository cashDrawerRepository,
        ICurrentSession session,
        IDelayProvider delay,
        INavigationService navigation,
        MainMenuViewModel returnTo)
    {
        _cashDrawerRepository = cashDrawerRepository;
        _session = session;
        _delay = delay;
        _navigation = navigation;
        _returnTo = returnTo;
    }

    public async Task LoadAsync()
    {
        var today = DateTime.Today;
        BusinessDateStr = today.ToString("yyyy-MM-dd");

        string posCd = _session.CurrentTerminal!.PosCode;
        var entry = await _cashDrawerRepository.GetAsync(posCd, today);

        if (entry is not null)
        {
            AmountInput = entry.OpeningAmount.ToString("N0");
            SavedAmountStr = CurrencyFormat.Format(entry.OpeningAmount);
        }
        else
        {
            AmountInput = string.Empty;
            SavedAmountStr = null;
        }
    }

    partial void OnAmountInputChanged(string value)
    {
        if (_isFormattingAmount) return;

        string digitsOnly = new string(value.Where(char.IsDigit).ToArray());
        string formatted = digitsOnly.Length == 0 ? string.Empty : decimal.Parse(digitsOnly).ToString("N0");

        if (formatted != value)
        {
            _isFormattingAmount = true;
            AmountInput = formatted;
            _isFormattingAmount = false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = null;

        string digitsOnly = new string(AmountInput.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length == 0 || !decimal.TryParse(digitsOnly, out decimal amount) || amount < 0)
        {
            ErrorMessage = "시작 현금 금액을 올바르게 입력해주세요";
            return;
        }

        string posCd = _session.CurrentTerminal!.PosCode;
        await _cashDrawerRepository.SaveOpeningAmountAsync(new CashDrawerEntry
        {
            PosCd = posCd,
            BusinessDate = DateTime.Today,
            OpeningAmount = amount,
            StaffCd = _session.CurrentStaff?.StaffCode,
        });

        SavedAmountStr = CurrencyFormat.Format(amount);
        ToastMessage = "저장되었습니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);
}
