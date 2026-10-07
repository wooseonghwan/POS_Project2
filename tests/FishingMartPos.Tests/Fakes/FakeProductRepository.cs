using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeProductRepository : IProductRepository
{
    private readonly List<Product> _products;

    public FakeProductRepository(IReadOnlyList<Product> products)
    {
        _products = products.ToList();
    }

    public List<string> DeactivatedBarcodes { get; } = new();
    public List<Product> SavedProducts { get; } = new();
    public List<(string PosCatCd, IReadOnlyList<string> Barcodes)> SortOrderUpdates { get; } = new();

    public Task<IReadOnlyList<Product>> GetActiveAsync() =>
        Task.FromResult((IReadOnlyList<Product>)_products);

    public Task DeactivateAsync(string barcode)
    {
        DeactivatedBarcodes.Add(barcode);
        _products.RemoveAll(p => p.Barcode == barcode);
        return Task.CompletedTask;
    }

    public Task SaveAsync(Product product)
    {
        SavedProducts.Add(product);
        _products.RemoveAll(p => p.Barcode == product.Barcode);
        _products.Add(product);
        return Task.CompletedTask;
    }

    public Task UpdateSortOrderAsync(string posCatCd, IReadOnlyList<string> orderedBarcodes)
    {
        SortOrderUpdates.Add((posCatCd, orderedBarcodes));

        for (int i = 0; i < orderedBarcodes.Count; i++)
        {
            int index = _products.FindIndex(p => p.Barcode == orderedBarcodes[i] && p.PosCatCd == posCatCd);
            if (index < 0) continue;

            var existing = _products[index];
            _products[index] = new Product
            {
                Barcode = existing.Barcode,
                PosCatCd = existing.PosCatCd,
                Name = existing.Name,
                Price = existing.Price,
                StockQty = existing.StockQty,
                PhotoPath = existing.PhotoPath,
                ShowInGrid = existing.ShowInGrid,
                SortNo = i,
            };
        }

        // 실제 ProductRepository의 ORDER BY poscat_cd, sort_no, name 과 동일하게 맞춰,
        // 재조회(GetActiveAsync) 시 바뀐 순서가 바로 반영되도록 한다.
        _products.Sort((a, b) =>
        {
            int byCategory = string.Compare(a.PosCatCd, b.PosCatCd, StringComparison.Ordinal);
            if (byCategory != 0) return byCategory;
            int bySortNo = a.SortNo.CompareTo(b.SortNo);
            if (bySortNo != 0) return bySortNo;
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return Task.CompletedTask;
    }
}
