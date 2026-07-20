using FishingMartPos.Domain;
using Xunit;

namespace FishingMartPos.Tests.Domain;

public class CartTests
{
    [Fact]
    public void Add_NewItem_CreatesLineWithQtyOne()
    {
        var cart = new Cart();

        cart.Add("B1", "지렁이", 5000);

        var line = Assert.Single(cart.Lines);
        Assert.Equal("B1", line.Barcode);
        Assert.Equal(1, line.Qty);
        Assert.Equal(5000, line.LineTotal);
    }

    [Fact]
    public void Add_ExistingItem_IncrementsQty()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Add("B1", "지렁이", 5000);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(2, line.Qty);
        Assert.Equal(10000, line.LineTotal);
    }

    [Fact]
    public void SetQty_ClampsToMinimumOne()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.SetQty("B1", 0);

        Assert.Equal(1, cart.Lines[0].Qty);
    }

    [Fact]
    public void Increment_And_Decrement_AdjustQty()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Increment("B1");
        cart.Increment("B1");
        cart.Decrement("B1");

        Assert.Equal(2, cart.Lines[0].Qty);
    }

    [Fact]
    public void Decrement_NeverGoesBelowOne()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Decrement("B1");
        cart.Decrement("B1");

        Assert.Equal(1, cart.Lines[0].Qty);
    }

    [Fact]
    public void Remove_DeletesLine()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Remove("B1");

        Assert.Empty(cart.Lines);
    }

    [Fact]
    public void Total_And_TotalQty_SumAcrossLines()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);
        cart.Add("B2", "냉동새우", 3000);
        cart.Increment("B2");

        Assert.Equal(11000, cart.Total);
        Assert.Equal(3, cart.TotalQty);
    }

    [Fact]
    public void Clear_EmptiesCart()
    {
        var cart = new Cart();
        cart.Add("B1", "지렁이", 5000);

        cart.Clear();

        Assert.Empty(cart.Lines);
        Assert.Equal(0, cart.Total);
    }
}
