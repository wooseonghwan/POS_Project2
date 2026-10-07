namespace FishingMartPos.Domain;

public static class BarcodeGenerator
{
    // 바코드를 입력하지 않고 등록하는(바코드 없는) 상품에 자동으로 매길 번호.
    // 70000부터 시작해서 1씩 증가 — 기존에 수동으로 입력해 둔 상품 바코드들
    // (10000번대 생미끼, 50000번대 생활용품, 100000번대 금액 상품 등)과 겹치지 않는
    // 비어 있는 구간이라 이 범위를 쓴다.
    private const int StartingValue = 70000;
    private const int MaxValue = 99999;

    public static string GenerateNext(IEnumerable<string> existingBarcodes)
    {
        var existing = new HashSet<string>(existingBarcodes);

        int candidate = StartingValue;
        foreach (var barcode in existing)
        {
            if (int.TryParse(barcode, out int value) && value >= StartingValue && value <= MaxValue && value + 1 > candidate)
            {
                candidate = value + 1;
            }
        }

        // 혹시 이 구간에 과거에 수동으로 등록된 바코드가 우연히 있더라도 절대 중복되지 않도록 한 번 더 확인한다.
        while (existing.Contains(candidate.ToString()))
        {
            candidate++;
        }

        return candidate.ToString();
    }
}
