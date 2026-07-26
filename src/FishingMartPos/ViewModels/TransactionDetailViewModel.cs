using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;

namespace FishingMartPos.ViewModels;

public sealed partial class TransactionDetailViewModel : ObservableObject
{
    private readonly string _currentPosCode;
    private readonly ISalesRepository _salesRepository;
    private readonly IVanPaymentGateway _vanGateway;
    private readonly ICashReceiptGateway _cashReceiptGateway;
    private readonly IReceiptPrinter _receiptPrinter;
    private readonly IDelayProvider _delay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanConvertToCashReceipt))]
    [NotifyPropertyChangedFor(nameof(SaleNoStr))]
    [NotifyPropertyChangedFor(nameof(SaleDtStr))]
    [NotifyPropertyChangedFor(nameof(PayTypeLabelStr))]
    [NotifyPropertyChangedFor(nameof(TotalAmtStr))]
    [NotifyPropertyChangedFor(nameof(InstallmentLabelStr))]
    [NotifyPropertyChangedFor(nameof(StatusLabelStr))]
    private SaleHeader _header;

    [ObservableProperty]
    private bool _isCancelling;

    [ObservableProperty]
    private bool _isConvertingToReceipt;

    [ObservableProperty]
    private bool _isReceiptConversionVisible;

    [ObservableProperty]
    private string _selectedReceiptType = "PERSONAL";

    [ObservableProperty]
    private string? _selectedReceiptMerchant;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isStatusError;

    public IReadOnlyList<SaleDetailLine> Lines { get; }

    public event Action? Changed;
    public event Action? CloseRequested;

    public TransactionDetailViewModel(
        SaleHeader header,
        IReadOnlyList<SaleDetailLine> lines,
        string currentPosCode,
        ISalesRepository salesRepository,
        IVanPaymentGateway vanGateway,
        ICashReceiptGateway cashReceiptGateway,
        IReceiptPrinter receiptPrinter,
        IDelayProvider delay)
    {
        _header = header;
        Lines = lines;
        _currentPosCode = currentPosCode;
        _salesRepository = salesRepository;
        _vanGateway = vanGateway;
        _cashReceiptGateway = cashReceiptGateway;
        _receiptPrinter = receiptPrinter;
        _delay = delay;
    }

    public bool CanCancel => Header.Status == "COMPLETE";
    public bool CanConvertToCashReceipt => Header.PayType == "CASH" && Header.CashReceiptType == "NONE" && Header.Status == "COMPLETE";
    public string SaleNoStr => Header.SaleNo.ToString();
    public string SaleDtStr => Header.SaleDt.ToString("yyyy-MM-dd HH:mm:ss");
    public string PayTypeLabelStr => Header.PayType switch
    {
        "CASH" => "현금",
        "CARD1" => "카드결제1",
        "CARD2" => "카드결제2",
        _ => Header.PayType,
    };
    public string TotalAmtStr => CurrencyFormat.Format(Header.TotalAmt);
    public string InstallmentLabelStr => Header.InstallmentMonths <= 0 ? "일시불" : $"{Header.InstallmentMonths}개월";
    public string StatusLabelStr => Header.Status == "CANCELLED" ? "취소됨" : "정상";

    [RelayCommand]
    private async Task Cancel()
    {
        if (!CanCancel || IsCancelling) return;

        IsCancelling = true;
        StatusMessage = null;
        try
        {
            if (Header.PayType != "CASH")
            {
                var result = await _vanGateway.RequestCancelAsync(new VanCancelRequest(
                    _currentPosCode, Header.PayType, Header.TotalAmt, Header.InstallmentMonths,
                    Header.VanApprovalNo!, Header.SaleDt.ToString("yyMMdd")));
                if (!result.IsCancelled)
                {
                    IsStatusError = true;
                    StatusMessage = result.ResponseMessage;
                    return;
                }
            }
            else if (Header.CashReceiptType != "NONE")
            {
                var result = await _cashReceiptGateway.RequestCancelAsync(new CashReceiptCancelRequest(
                    _currentPosCode, Header.CashReceiptMerchant!, Header.CashReceiptType, Header.TotalAmt,
                    Header.CashReceiptApprovalNo!, Header.CashReceiptApprovalDate!));
                if (!result.IsCancelled)
                {
                    IsStatusError = true;
                    StatusMessage = result.ResponseMessage;
                    return;
                }
            }

            try
            {
                await _salesRepository.CancelSaleAsync(Header.SaleNo);
            }
            catch (Exception)
            {
                IsStatusError = true;
                StatusMessage = "취소 처리 중 오류가 발생했습니다. 담당자에게 문의하세요";
                return;
            }

            Header = Header with { Status = "CANCELLED" };
            IsStatusError = false;
            StatusMessage = "취소되었습니다";
            Changed?.Invoke();
        }
        finally
        {
            IsCancelling = false;
        }
    }

    [RelayCommand]
    private void ShowReceiptConversion()
    {
        if (!CanConvertToCashReceipt) return;
        SelectedReceiptType = "PERSONAL";
        SelectedReceiptMerchant = null;
        StatusMessage = null;
        IsReceiptConversionVisible = true;
    }

    [RelayCommand]
    private void SelectReceiptType(string type) => SelectedReceiptType = type;

    [RelayCommand]
    private void SelectReceiptMerchant(string merchant) => SelectedReceiptMerchant = merchant;

    [RelayCommand]
    private void CancelReceiptConversion() => IsReceiptConversionVisible = false;

    [RelayCommand]
    private async Task ConfirmReceiptConversion()
    {
        if (SelectedReceiptMerchant is not string merchant || IsConvertingToReceipt) return;

        IsConvertingToReceipt = true;
        StatusMessage = null;
        try
        {
            var result = await _cashReceiptGateway.RequestIssueAsync(
                new CashReceiptRequest(_currentPosCode, merchant, SelectedReceiptType, Header.TotalAmt));
            if (!result.IsIssued)
            {
                IsStatusError = true;
                StatusMessage = result.ResponseMessage;
                return;
            }

            try
            {
                await _salesRepository.UpdateCashReceiptAsync(Header.SaleNo, SelectedReceiptType, merchant, result.ApprovalNo!, result.ApprovalDateYyMmDd!);
            }
            catch (Exception)
            {
                IsStatusError = true;
                StatusMessage = "현금영수증 갱신 중 오류가 발생했습니다. 담당자에게 문의하세요";
                return;
            }

            Header = Header with
            {
                CashReceiptType = SelectedReceiptType,
                CashReceiptMerchant = merchant,
                CashReceiptApprovalNo = result.ApprovalNo,
                CashReceiptApprovalDate = result.ApprovalDateYyMmDd,
            };
            IsReceiptConversionVisible = false;
            IsStatusError = false;
            StatusMessage = "현금영수증이 발급되었습니다";
            Changed?.Invoke();
        }
        finally
        {
            IsConvertingToReceipt = false;
        }
    }

    [RelayCommand]
    private async Task ReissueReceipt()
    {
        var document = ReceiptDocumentFactory.FromSale(Header, Lines);
        bool printed = await _receiptPrinter.PrintAsync(document);
        if (!printed)
        {
            IsStatusError = true;
            StatusMessage = "프린터 연동은 지원 예정입니다";
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
