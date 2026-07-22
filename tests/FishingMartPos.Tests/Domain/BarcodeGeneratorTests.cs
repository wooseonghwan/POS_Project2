using FishingMartPos.Domain;
using Xunit;

namespace FishingMartPos.Tests.Domain;

public class BarcodeGeneratorTests
{
    [Fact]
    public void GenerateNext_WithNoExistingBarcodes_StartsAtOne()
    {
        var result = BarcodeGenerator.GenerateNext(Array.Empty<string>());

        Assert.Equal("8800000020001", result);
    }

    [Fact]
    public void GenerateNext_WithExistingBarcodes_ReturnsMaxPlusOne()
    {
        var existing = new[] { "8800000020001", "8800000020050", "8800000020003" };

        var result = BarcodeGenerator.GenerateNext(existing);

        Assert.Equal("8800000020051", result);
    }

    [Fact]
    public void GenerateNext_IgnoresBarcodesWithDifferentPrefix()
    {
        var existing = new[] { "8800000020099", "9999999999999" };

        var result = BarcodeGenerator.GenerateNext(existing);

        Assert.Equal("8800000020100", result);
    }
}
