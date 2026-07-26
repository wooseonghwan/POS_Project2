using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class PaymentManagementViewModel : ObservableObject
{
    private readonly ISalesRepository _salesRepository;
    private readonly IVanPaymentGateway _vanGateway;
    private readonly ICashReceiptGateway _cashReceiptGateway;
    private readonly IReceiptPrinter _receiptPrinter;
    private readonly IDelayProvider _delay;
    private readonly ICurrentSession _session;
    private readonly INavigationService _navigation;
    private readonly MainMenuViewModel _returnTo;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private string _payTypeFilter = "ALL";

    [ObservableProperty]
    private string _approvalNoFilter = string.Empty;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private bool _isDetailVisible;

    [ObservableProperty]
    private TransactionDetailViewModel? _selectedDetail;

    public ObservableCollection<PaymentSummaryRowViewModel> Rows { get; } = new();

    public PaymentManagementViewModel(
        ISalesRepository salesRepository,
        IVanPaymentGateway vanGateway,
        ICashReceiptGateway cashReceiptGateway,
        IReceiptPrinter receiptPrinter,
        IDelayProvider delay,
        ICurrentSession session,
        INavigationService navigation,
        MainMenuViewModel returnTo)
    {
        _salesRepository = salesRepository;
        _vanGateway = vanGateway;
        _cashReceiptGateway = cashReceiptGateway;
        _receiptPrinter = receiptPrinter;
        _delay = delay;
        _session = session;
        _navigation = navigation;
        _returnTo = returnTo;
        DateTo = DateTime.Today;
        DateFrom = DateTime.Today.AddDays(-6);
    }

    public async Task LoadAsync() => await RefreshAsync();

    [RelayCommand]
    private async Task Search() => await RefreshAsync();

    [RelayCommand]
    private async Task SelectPayTypeFilter(string payType)
    {
        PayTypeFilter = payType;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var from = (DateFrom ?? DateTime.Today).Date;
        var to = (DateTo ?? DateTime.Today).Date.AddDays(1);
        string? payType = PayTypeFilter == "ALL" ? null : PayTypeFilter;
        string? approvalNo = string.IsNullOrWhiteSpace(ApprovalNoFilter) ? null : ApprovalNoFilter.Trim();

        var sales = await _salesRepository.SearchSalesAsync(from, to, payType, approvalNo);

        Rows.Clear();
        foreach (var sale in sales)
        {
            var captured = sale;
            Rows.Add(new PaymentSummaryRowViewModel
            {
                SaleNo = captured.SaleNo,
                SaleDtStr = captured.SaleDt.ToString("yyyy-MM-dd HH:mm"),
                PayTypeLabel = captured.PayType switch
                {
                    "CASH" => "현금",
                    "CARD1" => "카드결제1",
                    "CARD2" => "카드결제2",
                    _ => captured.PayType,
                },
                TotalAmtStr = CurrencyFormat.Format(captured.TotalAmt),
                StatusLabel = captured.Status == "CANCELLED" ? "취소됨" : "정상",
                SelectCommand = new AsyncRelayCommand(() => OpenDetailAsync(captured.SaleNo)),
            });
        }
        HasRows = Rows.Count > 0;
    }

    private async Task OpenDetailAsync(long saleNo)
    {
        var result = await _salesRepository.GetSaleWithLinesAsync(saleNo);
        if (result is null) return;

        var detail = new TransactionDetailViewModel(
            result.Value.Header, result.Value.Lines, _session.CurrentTerminal!.PosCode,
            _salesRepository, _vanGateway, _cashReceiptGateway, _receiptPrinter, _delay);
        detail.Changed += async () => await RefreshAsync();
        detail.CloseRequested += () => IsDetailVisible = false;

        SelectedDetail = detail;
        IsDetailVisible = true;
    }

    [RelayCommand]
    private void GoToMainMenu() => _navigation.NavigateTo(_returnTo);
}
