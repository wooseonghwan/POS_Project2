namespace FishingMartPos.Services;

public interface ISignatureConverter
{
    Task<string?> ConvertToHexAsync(byte[] bmpBytes);
}

public sealed class StubSignatureConverter : ISignatureConverter
{
    public Task<string?> ConvertToHexAsync(byte[] bmpBytes) => Task.FromResult<string?>("00");
}
