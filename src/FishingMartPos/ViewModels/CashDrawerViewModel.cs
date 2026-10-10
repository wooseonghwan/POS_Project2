using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

/// <summary>영업일 시작 현금(시재) 입력 화면. 지폐 종류별 매수를 입력하면 합계가 자동으로 계산된다.
/// 하루에 한 번, 포스단말별로 시작할 때 넣어두는 금액.</summary>
public sealed partial class CashDrawerViewModel : ObservableObject
{
    private readonly ICashDrawerRepository _cashDrawerRepository;
    private readonly ICurrentSession _session;
    private readonly IDelayProvider _delay;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;
    private readonly IActionLogger _actionLogger;

    private bool _isSanitizing;

    [ObservableProperty] private string _businessDateStr = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalAmountStr))]
    private string _count1000Input = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalAmountStr))]
    private string _count5000Input = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalAmountStr))]
    private string _count10000Input = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalAmountStr))]
    private string _count50000Input = string.Empty;

    // 오늘 이미 저장해 둔 금액(없으면 null). 지폐별 매수는 저장하지 않으므로, 이미 저장된 날 다시 들어오면
    // 매수 입력란은 비어 있고 이 합계만 참고용으로 보여준다.
    [ObservableProperty] private string? _savedAmountStr;
    [ObservableProperty] private string? _toastMessage;

    public string TotalAmountStr => CurrencyFormat.Format(ComputeTotal());

    public CashDrawerViewModel(
        ICashDrawerRepository cashDrawerRepository,
        ICurrentSession session,
        IDelayProvider delay,
        INavigationService navigation,
        MainMenuViewModel returnTo,
        IActionLogger? actionLogger = null)
    {
        _cashDrawerRepository = cashDrawerRepository;
        _session = session;
        _delay = delay;
        _navigation = navigation;
        _returnTo = returnTo;
        _actionLogger = actionLogger ?? NullActionLogger.Instance;
    }

    public async Task LoadAsync()
    {
        var today = DateTime.Today;
        BusinessDateStr = today.ToString("yyyy-MM-dd");

        Count1000Input = string.Empty;
        Count5000Input = string.Empty;
        Count10000Input = string.Empty;
        Count50000Input = string.Empty;

        string posCd = _session.CurrentTerminal!.PosCode;
        var entry = await _cashDrawerRepository.GetAsync(posCd, today);
        SavedAmountStr = entry is not null ? CurrencyFormat.Format(entry.OpeningAmount) : null;
    }

    // 매수는 숫자만 의미가 있으므로(음수/소수 불가), 숫자 아닌 문자는 입력 즉시 제거한다.
    // 네 입력란이 같은 로직을 쓰므로 공용 헬퍼로 묶었다 — Changed 콜백이 값을 다시 쓸 때 재귀적으로
    // 또 Changed가 불리는 걸 _isSanitizing으로 막는다(InventoryFormViewModel의 PriceInput과 같은 패턴).
    private void Sanitize(string rawValue, Action<string> setSanitized)
    {
        if (_isSanitizing) return;

        string digitsOnly = new string(rawValue.Where(char.IsDigit).ToArray());
        if (digitsOnly != rawValue)
        {
            _isSanitizing = true;
            setSanitized(digitsOnly);
            _isSanitizing = false;
        }
    }

    partial void OnCount1000InputChanged(string value) => Sanitize(value, v => Count1000Input = v);
    partial void OnCount5000InputChanged(string value) => Sanitize(value, v => Count5000Input = v);
    partial void OnCount10000InputChanged(string value) => Sanitize(value, v => Count10000Input = v);
    partial void OnCount50000InputChanged(string value) => Sanitize(value, v => Count50000Input = v);

    private static int ParseCount(string input) => int.TryParse(input, out int n) && n >= 0 ? n : 0;

    private decimal ComputeTotal() =>
        ParseCount(Count1000Input) * 1000m +
        ParseCount(Count5000Input) * 5000m +
        ParseCount(Count10000Input) * 10000m +
        ParseCount(Count50000Input) * 50000m;

    [RelayCommand]
    private async Task Save()
    {
        decimal amount = ComputeTotal();

        string posCd = _session.CurrentTerminal!.PosCode;
        await _cashDrawerRepository.SaveOpeningAmountAsync(new CashDrawerEntry
        {
            PosCd = posCd,
            BusinessDate = DateTime.Today,
            OpeningAmount = amount,
            StaffCd = _session.CurrentStaff?.StaffCode,
        });

        await _actionLogger.LogAsync("CASH_DRAWER_SAVE", $"시재입력: 포스={posCd}, 금액={amount:N0}원");

        SavedAmountStr = CurrencyFormat.Format(amount);
        ToastMessage = "저장되었습니다";
        await _delay.Delay(TimeSpan.FromMilliseconds(1200));
        ToastMessage = null;
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);
}
