using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeCodeRepository : ICodeRepository
{
    private readonly Dictionary<string, List<CodeItem>> _byGroup;

    public FakeCodeRepository(Dictionary<string, IReadOnlyList<CodeItem>> byGroup)
    {
        _byGroup = byGroup.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
    }

    public List<(string CodeGbn, string CodeCd)> DeletedCodes { get; } = new();

    public Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn) =>
        Task.FromResult(_byGroup.TryGetValue(codeGbn, out var list) ? (IReadOnlyList<CodeItem>)list : Array.Empty<CodeItem>());

    public Task AddAsync(string codeGbn, string codeCd, string codeNm)
    {
        if (!_byGroup.TryGetValue(codeGbn, out var list))
        {
            list = new List<CodeItem>();
            _byGroup[codeGbn] = list;
        }
        list.Add(new CodeItem { Code = codeCd, Name = codeNm, SortNo = 0 });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string codeGbn, string codeCd)
    {
        DeletedCodes.Add((codeGbn, codeCd));
        if (_byGroup.TryGetValue(codeGbn, out var list))
        {
            list.RemoveAll(c => c.Code == codeCd);
        }
        return Task.CompletedTask;
    }

    public Task UpdateSortOrderAsync(string codeGbn, IReadOnlyList<string> orderedCodeCds)
    {
        if (!_byGroup.TryGetValue(codeGbn, out var list))
        {
            return Task.CompletedTask;
        }

        var byCode = list.ToDictionary(c => c.Code);
        var reordered = new List<CodeItem>();
        for (int i = 0; i < orderedCodeCds.Count; i++)
        {
            if (byCode.TryGetValue(orderedCodeCds[i], out var existing))
            {
                reordered.Add(new CodeItem { Code = existing.Code, Name = existing.Name, SortNo = i, UseYn = existing.UseYn });
            }
        }
        _byGroup[codeGbn] = reordered;
        return Task.CompletedTask;
    }

    public Task SetUseYnAsync(string codeGbn, string codeCd, bool useYn)
    {
        if (_byGroup.TryGetValue(codeGbn, out var list))
        {
            int index = list.FindIndex(c => c.Code == codeCd);
            if (index >= 0)
            {
                var existing = list[index];
                list[index] = new CodeItem { Code = existing.Code, Name = existing.Name, SortNo = existing.SortNo, UseYn = useYn };
            }
        }
        return Task.CompletedTask;
    }
}
