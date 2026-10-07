using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class CashDrawerRepositoryTests
{
    private const string TestPosCd = "9";

    private static (ICashDrawerRepository repo, IDbConnectionFactory factory) Create()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return (new CashDrawerRepository(factory), factory);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNoRowExists()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM cash_drawer_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });

        var result = await repo.GetAsync(TestPosCd, DateTime.Today);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveOpeningAmountAsync_ThenGetAsync_ReturnsSavedValues()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM cash_drawer_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveOpeningAmountAsync(new CashDrawerEntry
            {
                PosCd = TestPosCd, BusinessDate = DateTime.Today, OpeningAmount = 50000, StaffCd = "ADMIN1",
            });

            var result = await repo.GetAsync(TestPosCd, DateTime.Today);

            Assert.NotNull(result);
            Assert.Equal(50000m, result!.OpeningAmount);
            Assert.Equal("ADMIN1", result.StaffCd);
            Assert.Equal(DateTime.Today, result.BusinessDate.Date);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM cash_drawer_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task SaveOpeningAmountAsync_Twice_OverwritesExistingRowForSameDay()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM cash_drawer_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveOpeningAmountAsync(new CashDrawerEntry
            {
                PosCd = TestPosCd, BusinessDate = DateTime.Today, OpeningAmount = 50000, StaffCd = "ADMIN1",
            });
            await repo.SaveOpeningAmountAsync(new CashDrawerEntry
            {
                PosCd = TestPosCd, BusinessDate = DateTime.Today, OpeningAmount = 70000, StaffCd = "STAFF1",
            });

            var result = await repo.GetAsync(TestPosCd, DateTime.Today);

            Assert.Equal(70000m, result!.OpeningAmount);
            Assert.Equal("STAFF1", result.StaffCd);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM cash_drawer_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task GetAsync_DoesNotReturnEntryFromADifferentDay()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM cash_drawer_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveOpeningAmountAsync(new CashDrawerEntry
            {
                PosCd = TestPosCd, BusinessDate = DateTime.Today.AddDays(-1), OpeningAmount = 30000, StaffCd = "ADMIN1",
            });

            var result = await repo.GetAsync(TestPosCd, DateTime.Today);

            Assert.Null(result);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM cash_drawer_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }
}
