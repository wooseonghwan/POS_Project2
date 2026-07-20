using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class HeldOrderRepositoryTests
{
    private static IHeldOrderRepository CreateRepository(out MySqlConnectionFactory factory)
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        factory = new MySqlConnectionFactory(config);
        return new HeldOrderRepository(factory);
    }

    [Fact]
    public async Task Hold_Then_GetHeld_Then_GetLines_Then_Delete_RoundTrips()
    {
        IHeldOrderRepository repository = CreateRepository(out _);
        var lines = new[]
        {
            new HeldOrderLine { Barcode = "8800000020001", ProductName = "지렁이", Qty = 2, UnitPrice = 5000 },
        };

        long holdNo = await repository.HoldAsync("1", "ADMIN1", lines);
        Assert.True(holdNo > 0);

        var heldList = await repository.GetHeldAsync("1");
        Assert.Contains(heldList, h => h.HoldNo == holdNo && h.Total == 10000);

        var fetchedLines = await repository.GetLinesAsync(holdNo);
        var fetchedLine = Assert.Single(fetchedLines);
        Assert.Equal("지렁이", fetchedLine.ProductName);
        Assert.Equal(2, fetchedLine.Qty);

        await repository.DeleteAsync(holdNo);

        var afterDelete = await repository.GetHeldAsync("1");
        Assert.DoesNotContain(afterDelete, h => h.HoldNo == holdNo);
    }
}
