namespace FishingMartPos.Services.Kicc;

public static class KiccResponseParser
{
    public static IReadOnlyDictionary<string, string> Parse(string rdata)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in rdata.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            result[pair[..eq]] = pair[(eq + 1)..];
        }
        return result;
    }
}
