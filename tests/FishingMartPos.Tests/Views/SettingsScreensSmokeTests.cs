using FishingMartPos.Views;
using Xunit;

namespace FishingMartPos.Tests.Views;

public class SettingsScreensSmokeTests
{
    [Fact]
    public void SettingsView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new SettingsView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void StaffListView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new StaffListView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void StaffFormView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new StaffFormView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void PrinterSettingsView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new PrinterSettingsView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void ReceiptSettingsView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new ReceiptSettingsView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void SystemInfoView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new SystemInfoView();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void CodeManageView_ConstructsWithoutException()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new CodeManageView();
            Assert.NotNull(view);
        });
    }
}
