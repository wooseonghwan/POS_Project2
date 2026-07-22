using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ICodeRepository
{
    Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn);
    Task AddAsync(string codeGbn, string codeCd, string codeNm);
    Task DeleteAsync(string codeGbn, string codeCd);
}
