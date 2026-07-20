using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class PosViewSmokeTests
{
    [Fact]
    public void PosView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new PosView();
            Assert.NotNull(view);
        });
    }
}
