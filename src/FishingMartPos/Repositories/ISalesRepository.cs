using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface ISalesRepository
{
    Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines);

    Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to);

    Task<SaleHeader?> GetLastCompletedSaleAsync(string posCd);

    Task<IReadOnlyList<SaleHeader>> SearchSalesAsync(DateTime from, DateTime to, string? payType, string? approvalNo);

    Task<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)?> GetSaleWithLinesAsync(long saleNo);

    Task CancelSaleAsync(long saleNo);

    Task UpdateCashReceiptAsync(long saleNo, string receiptType, string merchant, string approvalNo, string approvalDateYyMmDd);
}
