using FishingMartPos.Services;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class RandomVanOutcomeProviderTests
{
    [Fact]
    public void NextIsApproved_OverManyCalls_ReturnsBothOutcomes()
    {
        var provider = new RandomVanOutcomeProvider();

        var results = Enumerable.Range(0, 200).Select(_ => provider.NextIsApproved()).ToList();

        Assert.Contains(true, results);
        Assert.Contains(false, results);
    }
}
