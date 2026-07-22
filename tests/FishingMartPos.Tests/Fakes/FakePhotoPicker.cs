using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakePhotoPicker : IPhotoPicker
{
    public string? NextPickedPath { get; set; }

    public string? PickPhoto() => NextPickedPath;
}
