using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class MainMenuViewSmokeTests
{
    [Fact]
    public void MainMenuView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new MainMenuView();
            Assert.NotNull(view);
        });
    }
}
