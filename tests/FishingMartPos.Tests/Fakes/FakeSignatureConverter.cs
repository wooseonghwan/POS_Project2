using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSignatureConverter : ISignatureConverter
{
    private readonly string? _hexResult;
    public List<byte[]> ConvertedBytes { get; } = new();

    public FakeSignatureConverter(string? hexResult = "00")
    {
        _hexResult = hexResult;
    }

    public Task<string?> ConvertToHexAsync(byte[] bmpBytes)
    {
        ConvertedBytes.Add(bmpBytes);
        return Task.FromResult(_hexResult);
    }
}
