using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeCodeRepository : ICodeRepository
{
    private readonly Dictionary<string, IReadOnlyList<CodeItem>> _byGroup;

    public FakeCodeRepository(Dictionary<string, IReadOnlyList<CodeItem>> byGroup)
    {
        _byGroup = byGroup;
    }

    public Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn) =>
        Task.FromResult(_byGroup.TryGetValue(codeGbn, out var list) ? list : Array.Empty<CodeItem>());
}
