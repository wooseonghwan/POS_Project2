using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ICodeRepository
{
    Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn);
    Task AddAsync(string codeGbn, string codeCd, string codeNm);
    Task DeleteAsync(string codeGbn, string codeCd);

    /// <summary>주어진 순서(위에서부터)대로 sort_no를 0,1,2...로 다시 매겨 저장한다. 판매화면 메뉴 탭 노출 순서를 결정한다.</summary>
    Task UpdateSortOrderAsync(string codeGbn, IReadOnlyList<string> orderedCodeCds);

    /// <summary>해당 코드(주로 POS분류)의 판매화면 메뉴 탭 노출 여부를 켜거나 끈다. 코드/상품 데이터 자체는 그대로 유지된다.</summary>
    Task SetUseYnAsync(string codeGbn, string codeCd, bool useYn);
}
