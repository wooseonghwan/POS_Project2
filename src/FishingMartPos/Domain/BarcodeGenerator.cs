namespace FishingMartPos.Domain;

public static class BarcodeGenerator
{
    private const string Prefix = "880000002";

    public static string GenerateNext(IEnumerable<string> existingBarcodes)
    {
        int maxSequence = 0;
        foreach (var barcode in existingBarcodes)
        {
            if (barcode.Length != 13 || !barcode.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(barcode.AsSpan(Prefix.Length), out int sequence) && sequence > maxSequence)
            {
                maxSequence = sequence;
            }
        }

        return Prefix + (maxSequence + 1).ToString("D4");
    }
}
