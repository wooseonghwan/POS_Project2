namespace FishingMartPos.Services;

public sealed class RandomVanOutcomeProvider : IVanOutcomeProvider
{
    private const double ApprovalRate = 0.85;
    private readonly Random _random = new();

    public bool NextIsApproved() => _random.NextDouble() < ApprovalRate;
}
