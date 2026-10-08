using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ISalesRepository
{
    Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines);

    Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to);

    /// <summary>해당 포스의 가장 최근 거래를 상태(완료/취소)와 무관하게 반환한다. "직전정보" 재발행에 쓰임 —
    /// 마지막 거래가 취소된 경우에도 그 거래를 열어 영수증을 재발행할 수 있어야 한다.</summary>
    Task<SaleHeader?> GetLastSaleAsync(string posCd);

    Task<IReadOnlyList<SaleHeader>> SearchSalesAsync(DateTime from, DateTime to, string? payType, string? approvalNo);

    Task<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)?> GetSaleWithLinesAsync(long saleNo);

    Task CancelSaleAsync(long saleNo);

    Task UpdateCashReceiptAsync(long saleNo, string receiptType, string merchant, string approvalNo, string approvalDateYyMmDd);
}
