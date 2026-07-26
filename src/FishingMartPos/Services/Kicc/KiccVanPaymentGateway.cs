using FishingMartPos.Services;

namespace FishingMartPos.Services.Kicc;

public sealed class KiccVanPaymentGateway : IVanPaymentGateway
{
    private readonly IKiccPosClient _client;
    private readonly IReadOnlyDictionary<string, KiccMerchantConfig> _merchantsByPayType;

    public KiccVanPaymentGateway(IKiccPosClient client, IReadOnlyDictionary<string, KiccMerchantConfig> merchantsByPayType)
    {
        _client = client;
        _merchantsByPayType = merchantsByPayType;
    }

    public async Task<VanApprovalResult> RequestApprovalAsync(VanApprovalRequest request)
    {
        var merchant = _merchantsByPayType[request.PayType];
        var posTranNo = BuildPosTranNo(request.PosCode);
        var sendData = KiccMessageBuilder.BuildApprovalRequest(merchant, request.Amount, posTranNo, request.InstallmentMonths, request.SignatureHex);

        var raw = await _client.RequestAsync(0xFB, 0x14, 0x04, sendData);

        if (!raw.IsSuccess)
        {
            return new VanApprovalResult { IsApproved = false, ResponseMessage = raw.FailureMessage ?? "카드 승인 거절" };
        }

        var fields = KiccResponseParser.Parse(raw.Data!);
        if (fields.TryGetValue("R04", out var rc) && rc == "0000")
        {
            return new VanApprovalResult
            {
                IsApproved = true,
                ApprovalNo = fields.GetValueOrDefault("R09"),
                VanCode = "KICC",
                ResponseMessage = "카드 결제 완료",
            };
        }

        return new VanApprovalResult { IsApproved = false, ResponseMessage = "카드 승인 거절" };
    }

    public async Task<VanCancelResult> RequestCancelAsync(VanCancelRequest request)
    {
        var merchant = _merchantsByPayType[request.PayType];
        var posTranNo = BuildPosTranNo(request.PosCode);
        var sendData = KiccMessageBuilder.BuildCardCancelRequest(
            merchant, request.Amount, request.InstallmentMonths,
            request.OriginalApprovalNo, request.OriginalApprovalDateYyMmDd, posTranNo);

        var raw = await _client.RequestAsync(0xFB, 0x14, 0x04, sendData);

        if (!raw.IsSuccess)
        {
            return new VanCancelResult { IsCancelled = false, ResponseMessage = raw.FailureMessage ?? "카드 취소 거절" };
        }

        var fields = KiccResponseParser.Parse(raw.Data!);
        if (fields.TryGetValue("R04", out var rc) && rc == "0000")
        {
            return new VanCancelResult { IsCancelled = true, ResponseMessage = "카드 결제 취소 완료" };
        }

        return new VanCancelResult { IsCancelled = false, ResponseMessage = "카드 취소 거절" };
    }

    private static string BuildPosTranNo(string posCode) =>
        $"{posCode}{DateTime.Now:yyMMddHHmmss}{Random.Shared.Next(10, 99)}";
}
