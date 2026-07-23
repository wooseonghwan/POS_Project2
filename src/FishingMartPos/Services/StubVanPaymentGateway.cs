namespace FishingMartPos.Services;

public sealed class StubVanPaymentGateway : IVanPaymentGateway
{
    private static readonly string[] DeclineMessages =
    {
        "한도초과", "카드 조회 실패", "가맹점 정보 오류", "응답 시간 초과",
    };

    private readonly IDelayProvider _delay;
    private readonly IVanOutcomeProvider _outcomeProvider;
    private readonly Random _messageRandom = new();

    public StubVanPaymentGateway(IDelayProvider delay, IVanOutcomeProvider outcomeProvider)
    {
        _delay = delay;
        _outcomeProvider = outcomeProvider;
    }

    public async Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        await _delay.Delay(TimeSpan.FromMilliseconds(1500));

        if (_outcomeProvider.NextIsApproved())
        {
            return new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = DateTime.Now.ToString("yyyyMMddHHmmss"),
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            };
        }

        return new VanApprovalResult
        {
            IsApproved = false,
            ApprovalNo = null,
            VanCode = null,
            ResponseMessage = DeclineMessages[_messageRandom.Next(DeclineMessages.Length)],
        };
    }
}
