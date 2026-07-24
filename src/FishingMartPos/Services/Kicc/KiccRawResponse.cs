namespace FishingMartPos.Services.Kicc;

public sealed class KiccRawResponse
{
    public required bool IsSuccess { get; init; }
    public string? Data { get; init; }
    public string? FailureMessage { get; init; }

    public static KiccRawResponse Success(string data) => new() { IsSuccess = true, Data = data };

    public static KiccRawResponse Failure(string message) => new() { IsSuccess = false, FailureMessage = message };
}
