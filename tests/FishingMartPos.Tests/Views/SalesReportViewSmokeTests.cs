using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class SalesReportViewSmokeTests
{
    [Fact]
    public void SalesReportView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new SalesReportView();
            Assert.NotNull(view);
        });
    }
}
