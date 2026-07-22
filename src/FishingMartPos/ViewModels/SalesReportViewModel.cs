using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class SalesReportViewModel : ObservableObject
{
    private readonly ISalesRepository _salesRepository;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;

    [ObservableProperty]
    private bool _isDailyTab = true;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private string _totalAmountStr = "0원";

    [ObservableProperty]
    private string _totalCashStr = "0원";

    [ObservableProperty]
    private string _totalCard1Str = "0원";

    [ObservableProperty]
    private string _totalCard2Str = "0원";

    [ObservableProperty]
    private bool _hasRows;

    public ObservableCollection<SalesReportRowViewModel> Rows { get; } = new();

    public SalesReportViewModel(ISalesRepository salesRepository, INavigationService navigation, MainMenuViewModel returnTo)
    {
        _salesRepository = salesRepository;
        _navigation = navigation;
        _returnTo = returnTo;
        DateTo = DateTime.Today;
        DateFrom = DateTime.Today.AddDays(-6);
    }

    public async Task LoadAsync() => await RefreshAsync();

    [RelayCommand]
    private async Task SelectDailyTab()
    {
        IsDailyTab = true;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SelectMonthlyTab()
    {
        IsDailyTab = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync();

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);

    private async Task RefreshAsync()
    {
        if (DateFrom is not { } dateFrom || DateTo is not { } dateTo)
        {
            return;
        }

        DateTime queryFrom;
        DateTime queryTo;
        if (IsDailyTab)
        {
            queryFrom = dateFrom.Date;
            queryTo = dateTo.Date.AddDays(1);
        }
        else
        {
            queryFrom = new DateTime(dateFrom.Year, dateFrom.Month, 1);
            queryTo = new DateTime(dateTo.Year, dateTo.Month, 1).AddMonths(1);
        }

        var sales = await _salesRepository.GetCompletedSalesAsync(queryFrom, queryTo);

        var groups = IsDailyTab
            ? sales.GroupBy(s => s.SaleDt.Date)
            : sales.GroupBy(s => new DateTime(s.SaleDt.Year, s.SaleDt.Month, 1));

        var rows = groups
            .OrderByDescending(g => g.Key)
            .Select(g => new SalesReportRowViewModel
            {
                Label = IsDailyTab ? g.Key.ToString("yyyy-MM-dd") : g.Key.ToString("yyyy-MM") + " 월",
                CountStr = g.Count().ToString("N0") + "건",
                CashStr = CurrencyFormat.Format(g.Where(s => s.PayType == "CASH").Sum(s => s.TotalAmt)),
                Card1Str = CurrencyFormat.Format(g.Where(s => s.PayType == "CARD1").Sum(s => s.TotalAmt)),
                Card2Str = CurrencyFormat.Format(g.Where(s => s.PayType == "CARD2").Sum(s => s.TotalAmt)),
                AmountStr = CurrencyFormat.Format(g.Sum(s => s.TotalAmt)),
            })
            .ToList();

        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
        HasRows = Rows.Count > 0;

        TotalAmountStr = CurrencyFormat.Format(sales.Sum(s => s.TotalAmt));
        TotalCashStr = CurrencyFormat.Format(sales.Where(s => s.PayType == "CASH").Sum(s => s.TotalAmt));
        TotalCard1Str = CurrencyFormat.Format(sales.Where(s => s.PayType == "CARD1").Sum(s => s.TotalAmt));
        TotalCard2Str = CurrencyFormat.Format(sales.Where(s => s.PayType == "CARD2").Sum(s => s.TotalAmt));
    }
}
