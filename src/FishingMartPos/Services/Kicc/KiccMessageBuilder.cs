namespace FishingMartPos.Services.Kicc;

public static class KiccMessageBuilder
{
    // 전문버전. 신규개발 권장값은 "002"이지만, 실기기 연동 테스트에서 단말기가 이 값을 받고도
    // 아무 응답을 안 보내는 현상이 확인되어(전문버전 미지원으로 조용히 무시하는 것으로 추정) "001"로 낮춘다.
    // 실물 단말기가 "002"를 지원하는 것으로 확인되면 이 상수만 되돌리면 된다.
    private const string ProtocolVersion = "001";

    public static string BuildApprovalRequest(KiccMerchantConfig merchant, decimal amount, string posTranNo, int installmentMonths = 0, string? signatureHex = null)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string installmentCode = installmentMonths.ToString("00");
        string signatureFields = signatureHex is not null ? $"S30=1;S31={signatureHex};" : string.Empty;
        return $"S00={ProtocolVersion};S01=D1;S02=40;S03={merchant.Tid};S04={merchant.BusinessNo};" +
               $"S09={installmentCode};S10={(int)amount};S15=0;S16={vat};S23={posTranNo};{signatureFields}";
    }

    public static string BuildCardCancelRequest(
        KiccMerchantConfig merchant, decimal amount, int installmentMonths,
        string originalApprovalNo, string originalApprovalDateYyMmDd, string posTranNo)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string installmentCode = installmentMonths.ToString("00");
        return $"S00={ProtocolVersion};S01=D4;S02=40;S03={merchant.Tid};S04={merchant.BusinessNo};" +
               $"S09={installmentCode};S10={(int)amount};S12={originalApprovalNo};S13={originalApprovalDateYyMmDd};S15=0;S16={vat};S23={posTranNo};";
    }

    public static string BuildCashReceiptIssueRequest(KiccMerchantConfig merchant, decimal amount, string receiptType, string posTranNo)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string receiptTypeCode = ReceiptTypeCode(receiptType);
        return $"S00={ProtocolVersion};S01=B1;S02=40;S03={merchant.Tid};" +
               $"S09=00;S10={(int)amount};S11={receiptTypeCode};S15=0;S16={vat};S23={posTranNo};";
    }

    public static string BuildCashReceiptCancelRequest(
        KiccMerchantConfig merchant, decimal amount, string receiptType,
        string originalApprovalNo, string originalApprovalDateYyMmDd, string posTranNo)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string receiptTypeCode = ReceiptTypeCode(receiptType);
        return $"S00={ProtocolVersion};S01=B2;S02=40;S03={merchant.Tid};" +
               $"S09=00;S10={(int)amount};S11={receiptTypeCode};S12={originalApprovalNo};S13={originalApprovalDateYyMmDd};S15=0;S16={vat};S23={posTranNo};";
    }

    private static string ReceiptTypeCode(string receiptType) => receiptType switch
    {
        "PERSONAL" => "00",
        "BUSINESS" => "01",
        _ => throw new ArgumentOutOfRangeException(nameof(receiptType), receiptType, "PERSONAL 또는 BUSINESS만 지원합니다"),
    };
}
