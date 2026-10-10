using FishingMartPos.Navigation;
using FishingMartPos.Tests.Fakes;
using Xunit;

namespace FishingMartPos.Tests.Navigation;

public class NavigationServiceTests
{
    [Fact]
    public void CurrentViewModel_IsNull_Initially()
    {
        INavigationService navigation = new NavigationService();

        Assert.Null(navigation.CurrentViewModel);
    }

    [Fact]
    public void NavigateTo_SetsCurrentViewModel()
    {
        INavigationService navigation = new NavigationService();
        var target = new object();

        navigation.NavigateTo(target);

        Assert.Same(target, navigation.CurrentViewModel);
    }

    [Fact]
    public void NavigateTo_RaisesCurrentViewModelChanged()
    {
        INavigationService navigation = new NavigationService();
        bool raised = false;
        navigation.CurrentViewModelChanged += (_, _) => raised = true;

        navigation.NavigateTo(new object());

        Assert.True(raised);
    }

    [Fact]
    public async Task NavigateTo_LogsNavigateAction()
    {
        var actionLogger = new FakeActionLogger();
        INavigationService navigation = new NavigationService(actionLogger);

        navigation.NavigateTo("타겟 화면");
        // NavigateTo는 로그를 fire-and-forget으로 남긴다 — 비동기 완료를 기다려준다(FakeActionLogger는
        // 동기 완료지만, 실제 구현도 Task를 반환하므로 일관되게 처리).
        await Task.Yield();

        var call = Assert.Single(actionLogger.Calls);
        Assert.Equal("NAVIGATE", call.ActionType);
        Assert.Contains("String", call.Detail);
    }
}
