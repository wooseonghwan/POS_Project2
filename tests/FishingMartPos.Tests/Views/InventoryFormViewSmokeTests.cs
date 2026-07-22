using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class InventoryFormViewSmokeTests
{
    [Fact]
    public void InventoryFormView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new InventoryFormView();
            Assert.NotNull(view);
        });
    }
}
