namespace FishingMartPos.Services;

public sealed class StubCashReceiptGateway : ICashReceiptGateway
{
    private static readonly string[] DeclineMessages =
    {
        "현금영수증 발급 실패", "단말기 통신 오류", "응답 시간 초과",
    };

    private readonly IDelayProvider _delay;
    private readonly IVanOutcomeProvider _outcomeProvider;
    private readonly Random _messageRandom = new();

    public StubCashReceiptGateway(IDelayProvider delay, IVanOutcomeProvider outcomeProvider)
    {
        _delay = delay;
        _outcomeProvider = outcomeProvider;
    }

    public async Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request)
    {
        await _delay.Delay(TimeSpan.FromMilliseconds(1500));

        if (_outcomeProvider.NextIsApproved())
        {
            return new CashReceiptResult
            {
                IsIssued = true,
                ApprovalNo = DateTime.Now.ToString("yyyyMMddHHmmss"),
                ApprovalDateYyMmDd = DateTime.Now.ToString("yyMMdd"),
                ResponseMessage = "현금영수증 발급 완료",
            };
        }

        return new CashReceiptResult
        {
            IsIssued = false,
            ApprovalNo = null,
            ApprovalDateYyMmDd = null,
            ResponseMessage = DeclineMessages[_messageRandom.Next(DeclineMessages.Length)],
        };
    }
}
