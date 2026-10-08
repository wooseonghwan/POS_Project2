using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSalesRepository : ISalesRepository
{
    public List<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)> CreatedSales { get; } = new();
    public List<(DateTime From, DateTime To)> GetCompletedSalesCalls { get; } = new();
    public List<long> CancelledSaleNos { get; } = new();
    public List<(long SaleNo, string ReceiptType, string Merchant, string ApprovalNo, string ApprovalDateYyMmDd)> CashReceiptUpdates { get; } = new();

    public bool ThrowOnCancelSale { get; set; }
    public bool ThrowOnUpdateCashReceipt { get; set; }

    private long _nextSaleNo = 1;
    private IReadOnlyList<SaleHeader> _completedSales = Array.Empty<SaleHeader>();
    private SaleHeader? _lastSale;
    private readonly Dictionary<long, (SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)> _salesByNo = new();

    public Task<long> CreateSaleAsync(SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        CreatedSales.Add((header, lines));
        return Task.FromResult(_nextSaleNo++);
    }

    public void SeedCompletedSales(IReadOnlyList<SaleHeader> sales)
    {
        _completedSales = sales;
    }

    public void SeedLastSale(SaleHeader? sale)
    {
        _lastSale = sale;
    }

    public void SeedSaleWithLines(long saleNo, SaleHeader header, IReadOnlyList<SaleDetailLine> lines)
    {
        _salesByNo[saleNo] = (header, lines);
    }

    public Task<IReadOnlyList<SaleHeader>> GetCompletedSalesAsync(DateTime from, DateTime to)
    {
        GetCompletedSalesCalls.Add((from, to));
        var filtered = _completedSales.Where(s => s.SaleDt >= from && s.SaleDt < to).ToList();
        return Task.FromResult<IReadOnlyList<SaleHeader>>(filtered);
    }

    public Task<SaleHeader?> GetLastSaleAsync(string posCd) => Task.FromResult(_lastSale);

    public Task<IReadOnlyList<SaleHeader>> SearchSalesAsync(DateTime from, DateTime to, string? payType, string? approvalNo)
    {
        var filtered = _completedSales
            .Where(s => s.SaleDt >= from && s.SaleDt < to)
            .Where(s => payType is null || s.PayType == payType)
            .Where(s => approvalNo is null || s.VanApprovalNo == approvalNo || s.CashReceiptApprovalNo == approvalNo)
            .ToList();
        return Task.FromResult<IReadOnlyList<SaleHeader>>(filtered);
    }

    public Task<(SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)?> GetSaleWithLinesAsync(long saleNo)
    {
        (SaleHeader Header, IReadOnlyList<SaleDetailLine> Lines)? result =
            _salesByNo.TryGetValue(saleNo, out var value) ? value : null;
        return Task.FromResult(result);
    }

    public Task CancelSaleAsync(long saleNo)
    {
        if (ThrowOnCancelSale)
        {
            throw new InvalidOperationException("Simulated DB failure on CancelSaleAsync");
        }
        CancelledSaleNos.Add(saleNo);
        return Task.CompletedTask;
    }

    public Task UpdateCashReceiptAsync(long saleNo, string receiptType, string merchant, string approvalNo, string approvalDateYyMmDd)
    {
        if (ThrowOnUpdateCashReceipt)
        {
            throw new InvalidOperationException("Simulated DB failure on UpdateCashReceiptAsync");
        }
        CashReceiptUpdates.Add((saleNo, receiptType, merchant, approvalNo, approvalDateYyMmDd));
        return Task.CompletedTask;
    }
}
