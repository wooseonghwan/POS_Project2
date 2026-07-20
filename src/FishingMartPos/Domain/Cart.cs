using FishingMartPos.Models;

namespace FishingMartPos.Domain;

public sealed class Cart
{
    private readonly List<CartLine> _lines = new();

    public IReadOnlyList<CartLine> Lines => _lines;
    public decimal Total => _lines.Sum(l => l.LineTotal);
    public int TotalQty => _lines.Sum(l => l.Qty);

    public void Add(string barcode, string name, decimal price)
    {
        var existing = _lines.FirstOrDefault(l => l.Barcode == barcode);
        if (existing is not null)
        {
            existing.Qty += 1;
            return;
        }

        _lines.Add(new CartLine { Barcode = barcode, Name = name, Price = price, Qty = 1 });
    }

    public void AddExisting(string barcode, string name, decimal price, int qty)
    {
        var existing = _lines.FirstOrDefault(l => l.Barcode == barcode);
        if (existing is not null)
        {
            existing.Qty += qty;
            return;
        }

        _lines.Add(new CartLine { Barcode = barcode, Name = name, Price = price, Qty = qty });
    }

    public void SetQty(string barcode, int qty)
    {
        var line = _lines.FirstOrDefault(l => l.Barcode == barcode);
        if (line is null) return;
        line.Qty = Math.Max(1, qty);
    }

    public void Increment(string barcode) => SetQty(barcode, GetQty(barcode) + 1);

    public void Decrement(string barcode) => SetQty(barcode, GetQty(barcode) - 1);

    public void Remove(string barcode) => _lines.RemoveAll(l => l.Barcode == barcode);

    public void Clear() => _lines.Clear();

    private int GetQty(string barcode) => _lines.FirstOrDefault(l => l.Barcode == barcode)?.Qty ?? 1;
}
