namespace FishingMartPos.Theme;

public static class CurrencyFormat
{
    public static string Format(decimal amount) => amount.ToString("N0") + "원";
}
