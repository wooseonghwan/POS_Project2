namespace FishingMartPos.Services;

/// <summary>ViewModel 생성자에 actionLogger를 안 넘겼을 때(기존 테스트 코드 등) 쓰는 아무것도
/// 하지 않는 기본값. 실제 앱(App.xaml.cs)에서는 항상 진짜 <see cref="ActionLogger"/>를 넘긴다.</summary>
public sealed class NullActionLogger : IActionLogger
{
    public static readonly NullActionLogger Instance = new();

    private NullActionLogger()
    {
    }

    public Task LogAsync(string actionType, string? detail = null, bool success = true, string? errorMessage = null) =>
        Task.CompletedTask;
}
