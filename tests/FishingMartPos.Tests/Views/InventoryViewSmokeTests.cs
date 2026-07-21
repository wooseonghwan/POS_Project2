using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class InventoryViewSmokeTests
{
    [Fact]
    public void InventoryView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new InventoryView();
            Assert.NotNull(view);
        });
    }
}
