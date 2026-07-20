using FishingMartPos.Security;
using Xunit;

namespace FishingMartPos.Tests.Security;

public class PinHasherTests
{
    [Fact]
    public void SamePinProducesSameHash()
    {
        Assert.Equal(PinHasher.Hash("0000"), PinHasher.Hash("0000"));
    }

    [Fact]
    public void DifferentPinProducesDifferentHash()
    {
        Assert.NotEqual(PinHasher.Hash("0000"), PinHasher.Hash("1234"));
    }

    [Fact]
    public void HashIsSixtyFourHexCharacters()
    {
        string hash = PinHasher.Hash("0000");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void HashAlgorithmMatchesSha2WithColonSeparatedSalt()
    {
        // MariaDB의 SHA2('0000:fishingmart-pos-salt', 256)과 동일한 결과가 나와야 한다.
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] expectedBytes = sha256.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes("0000:fishingmart-pos-salt"));
        string expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        Assert.Equal(expected, PinHasher.Hash("0000"));
    }
}
