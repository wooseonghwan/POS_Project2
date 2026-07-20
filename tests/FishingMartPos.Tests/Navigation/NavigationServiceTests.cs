using FishingMartPos.Navigation;
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
}
