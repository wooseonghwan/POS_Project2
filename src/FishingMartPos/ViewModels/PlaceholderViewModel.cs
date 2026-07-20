using CommunityToolkit.Mvvm.ComponentModel;

namespace FishingMartPos.ViewModels;

public sealed partial class PlaceholderViewModel : ObservableObject
{
    public PlaceholderViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
}
