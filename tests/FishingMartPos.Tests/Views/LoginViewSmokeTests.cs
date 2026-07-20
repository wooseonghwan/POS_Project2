using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class LoginViewSmokeTests
{
    [Fact]
    public void LoginView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new LoginView();
            Assert.NotNull(view);
        });
    }
}
