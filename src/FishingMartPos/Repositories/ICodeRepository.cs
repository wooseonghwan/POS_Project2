using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ICodeRepository
{
    Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn);
}
