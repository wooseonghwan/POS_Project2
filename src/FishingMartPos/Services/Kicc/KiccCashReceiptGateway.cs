using FishingMartPos.Services;

namespace FishingMartPos.Services.Kicc;

public sealed class KiccCashReceiptGateway : ICashReceiptGateway
{
    private readonly IKiccPosClient _client;
    private readonly IReadOnlyDictionary<string, KiccMerchantConfig> _merchantsByPayType;

    public KiccCashReceiptGateway(IKiccPosClient client, IReadOnlyDictionary<string, KiccMerchantConfig> merchantsByPayType)
    {
        _client = client;
        _merchantsByPayType = merchantsByPayType;
    }

    public async Task<CashReceiptResult> RequestIssueAsync(CashReceiptRequest request)
    {
        var merchant = _merchantsByPayType[request.MerchantPayType];
        var posTranNo = BuildPosTranNo(merchant.Tid);
        var sendData = KiccMessageBuilder.BuildCashReceiptIssueRequest(merchant, request.Amount, request.ReceiptType, posTranNo);

        var raw = await _client.RequestAsync(0xFB, 0x14, 0x04, sendData);

        if (!raw.IsSuccess)
        {
            return new CashReceiptResult { IsIssued = false, ResponseMessage = raw.FailureMessage ?? "현금영수증 발급 거절" };
        }

        var fields = KiccResponseParser.Parse(raw.Data!);
        if (fields.TryGetValue("R04", out var rc) && rc == "0000")
        {
            string? approvalDateTime = fields.GetValueOrDefault("R07"); // YYMMDDhhmmssN
            return new CashReceiptResult
            {
                IsIssued = true,
                ApprovalNo = fields.GetValueOrDefault("R09"),
                ApprovalDateYyMmDd = approvalDateTime is { Length: >= 6 } ? approvalDateTime[..6] : null,
                ResponseMessage = "현금영수증 발급 완료",
            };
        }

        return new CashReceiptResult { IsIssued = false, ResponseMessage = "현금영수증 발급 거절" };
    }

    private static string BuildPosTranNo(string tid) =>
        $"{tid}{DateTime.Now:yyMMddHHmmss}{Random.Shared.Next(10, 99)}";
}
