using FishingMartPos.Services;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeVanOutcomeProvider : IVanOutcomeProvider
{
    private readonly bool _isApproved;

    public FakeVanOutcomeProvider(bool isApproved)
    {
        _isApproved = isApproved;
    }

    public bool NextIsApproved() => _isApproved;
}
