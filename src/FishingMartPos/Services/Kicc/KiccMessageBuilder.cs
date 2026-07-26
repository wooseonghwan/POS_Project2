namespace FishingMartPos.Services.Kicc;

public static class KiccMessageBuilder
{
    public static string BuildApprovalRequest(KiccMerchantConfig merchant, decimal amount, string posTranNo, int installmentMonths = 0)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string installmentCode = installmentMonths.ToString("00");
        return $"S00=002;S01=D1;S02=40;S03={merchant.Tid};S04={merchant.BusinessNo};" +
               $"S09={installmentCode};S10={(int)amount};S15=0;S16={vat};S23={posTranNo};";
    }

    public static string BuildCashReceiptIssueRequest(KiccMerchantConfig merchant, decimal amount, string receiptType, string posTranNo)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string receiptTypeCode = ReceiptTypeCode(receiptType);
        return $"S00=002;S01=B1;S02=40;S03={merchant.Tid};" +
               $"S09=00;S10={(int)amount};S11={receiptTypeCode};S15=0;S16={vat};S23={posTranNo};";
    }

    public static string BuildCashReceiptCancelRequest(
        KiccMerchantConfig merchant, decimal amount, string receiptType,
        string originalApprovalNo, string originalApprovalDateYyMmDd, string posTranNo)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        string receiptTypeCode = ReceiptTypeCode(receiptType);
        return $"S00=002;S01=B2;S02=40;S03={merchant.Tid};" +
               $"S09=00;S10={(int)amount};S11={receiptTypeCode};S12={originalApprovalNo};S13={originalApprovalDateYyMmDd};S15=0;S16={vat};S23={posTranNo};";
    }

    private static string ReceiptTypeCode(string receiptType) => receiptType switch
    {
        "PERSONAL" => "00",
        "BUSINESS" => "01",
        _ => throw new ArgumentOutOfRangeException(nameof(receiptType), receiptType, "PERSONAL 또는 BUSINESS만 지원합니다"),
    };
}
