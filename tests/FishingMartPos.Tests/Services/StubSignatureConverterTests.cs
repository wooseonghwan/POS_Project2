using FishingMartPos.Services;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class StubSignatureConverterTests
{
    [Fact]
    public async Task ConvertToHexAsync_ReturnsFixedDummyHex()
    {
        var converter = new StubSignatureConverter();

        var result = await converter.ConvertToHexAsync(new byte[] { 1, 2, 3 });

        Assert.Equal("00", result);
    }
}
