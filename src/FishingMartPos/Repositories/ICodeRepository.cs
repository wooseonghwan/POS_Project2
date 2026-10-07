using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ICodeRepository
{
    Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn);
    Task AddAsync(string codeGbn, string codeCd, string codeNm);
    Task DeleteAsync(string codeGbn, string codeCd);

    /// <summary>주어진 순서(위에서부터)대로 sort_no를 0,1,2...로 다시 매겨 저장한다. 판매화면 메뉴 탭 노출 순서를 결정한다.</summary>
    Task UpdateSortOrderAsync(string codeGbn, IReadOnlyList<string> orderedCodeCds);
}
