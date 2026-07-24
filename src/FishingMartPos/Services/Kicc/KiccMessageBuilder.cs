namespace FishingMartPos.Services.Kicc;

public static class KiccMessageBuilder
{
    public static string BuildApprovalRequest(KiccMerchantConfig merchant, decimal amount, string posTranNo)
    {
        var vat = (int)Math.Round(amount / 11m, MidpointRounding.AwayFromZero);
        return $"S00=002;S01=D1;S02=40;S03={merchant.Tid};S04={merchant.BusinessNo};" +
               $"S09=00;S10={(int)amount};S15=0;S16={vat};S23={posTranNo};";
    }
}
