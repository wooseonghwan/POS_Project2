using Dapper;
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

    [Fact]
    public async Task Hold_MultipleLinesAndMultipleHolds_AggregatesAndDisambiguatesCorrectly()
    {
        IHeldOrderRepository repository = CreateRepository(out _);

        // Hold A: two detail lines for the same hold_no.
        // 지렁이 2 * 5000 = 10000, 냉동새우 3 * 5000 = 15000 -> Total 25000
        var holdALines = new[]
        {
            new HeldOrderLine { Barcode = "8800000020001", ProductName = "지렁이", Qty = 2, UnitPrice = 5000 },
            new HeldOrderLine { Barcode = "8800000020002", ProductName = "냉동새우", Qty = 3, UnitPrice = 5000 },
        };

        // Hold B: a single different line, held concurrently on the same pos_cd.
        // 냉동새우 1 * 5000 = 5000
        var holdBLines = new[]
        {
            new HeldOrderLine { Barcode = "8800000020002", ProductName = "냉동새우", Qty = 1, UnitPrice = 5000 },
        };

        long holdNoA = 0;
        long holdNoB = 0;
        try
        {
            holdNoA = await repository.HoldAsync("1", "ADMIN1", holdALines);
            holdNoB = await repository.HoldAsync("1", "ADMIN1", holdBLines);
            Assert.True(holdNoA > 0);
            Assert.True(holdNoB > 0);
            Assert.NotEqual(holdNoA, holdNoB);

            var heldList = await repository.GetHeldAsync("1");

            // Gap #1: SUM/GROUP BY over multiple detail rows for the same hold_no must not
            // be inflated or miscomputed by the LEFT JOIN.
            Assert.Contains(heldList, h => h.HoldNo == holdNoA && h.Total == 25000);

            // Gap #4: multiple concurrent held orders for the same pos_cd must both appear,
            // each with its own correct total (correct hold_no disambiguation).
            Assert.Contains(heldList, h => h.HoldNo == holdNoB && h.Total == 5000);

            var countForA = heldList.Count(h => h.HoldNo == holdNoA);
            var countForB = heldList.Count(h => h.HoldNo == holdNoB);
            Assert.Equal(1, countForA);
            Assert.Equal(1, countForB);

            // Gap #2: sequential line_no assignment across more than one line in the same hold.
            var linesForA = await repository.GetLinesAsync(holdNoA);
            Assert.Equal(2, linesForA.Count);
            Assert.Equal("지렁이", linesForA[0].ProductName);
            Assert.Equal(2, linesForA[0].Qty);
            Assert.Equal("냉동새우", linesForA[1].ProductName);
            Assert.Equal(3, linesForA[1].Qty);

            var linesForB = await repository.GetLinesAsync(holdNoB);
            var singleLineB = Assert.Single(linesForB);
            Assert.Equal("냉동새우", singleLineB.ProductName);
            Assert.Equal(1, singleLineB.Qty);
        }
        finally
        {
            if (holdNoA > 0)
            {
                await repository.DeleteAsync(holdNoA);
            }

            if (holdNoB > 0)
            {
                await repository.DeleteAsync(holdNoB);
            }
        }
    }

    [Fact]
    public async Task GetHeldAsync_ExcludesRowsWhoseStatusIsNotHeld()
    {
        IHeldOrderRepository repository = CreateRepository(out MySqlConnectionFactory factory);

        var lines = new[]
        {
            new HeldOrderLine { Barcode = "8800000020001", ProductName = "지렁이", Qty = 1, UnitPrice = 5000 },
        };

        long holdNo = 0;
        try
        {
            holdNo = await repository.HoldAsync("1", "ADMIN1", lines);
            Assert.True(holdNo > 0);

            // Sanity check: it is visible while status = 'HELD'.
            var heldBefore = await repository.GetHeldAsync("1");
            Assert.Contains(heldBefore, h => h.HoldNo == holdNo);

            // Simulate a row whose status has moved away from 'HELD' (e.g. after a future
            // RecallAsync/MarkRecalled operation), using a direct connection since no such
            // API exists yet.
            using (var connection = factory.CreateOpenConnection())
            {
                await connection.ExecuteAsync(
                    "UPDATE held_order_tb SET status = 'RECALLED' WHERE hold_no = @HoldNo",
                    new { HoldNo = holdNo });
            }

            // Gap #3: the WHERE status = 'HELD' filter must actually exclude non-HELD rows.
            var heldAfter = await repository.GetHeldAsync("1");
            Assert.DoesNotContain(heldAfter, h => h.HoldNo == holdNo);
        }
        finally
        {
            if (holdNo > 0)
            {
                await repository.DeleteAsync(holdNo);
            }
        }
    }
}
