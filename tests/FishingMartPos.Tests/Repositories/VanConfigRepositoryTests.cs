using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class VanConfigRepositoryTests
{
    private const string TestPosCd = "9";

    private static (IVanConfigRepository repo, IDbConnectionFactory factory) Create()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return (new VanConfigRepository(factory), factory);
    }

    [Fact]
    public async Task GetByPosCodeAsync_ReturnsEachPayTypeRowSeparately()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM van_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO van_config_tb (pos_cd, van_code, pay_type, terminal_id, business_no)
                VALUES (@PosCd, 'KICC', 'CARD1', '1111111', '1111111111'), (@PosCd, 'KICC', 'CARD2', '2222222', '2222222222')
                """,
                new { PosCd = TestPosCd });

            var rows = await repo.GetByPosCodeAsync(TestPosCd);

            Assert.Equal(2, rows.Count);
            var card1 = Assert.Single(rows, r => r.PayType == "CARD1");
            Assert.Equal("1111111", card1.TerminalId);
            Assert.Equal("1111111111", card1.BusinessNo);
            var card2 = Assert.Single(rows, r => r.PayType == "CARD2");
            Assert.Equal("2222222", card2.TerminalId);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM van_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task GetByPosCodeAsync_SeedData_ReturnsDaewonMerchantsForPos1()
    {
        var (repo, _) = Create();

        var rows = await repo.GetByPosCodeAsync("1");

        var card1 = Assert.Single(rows, r => r.PayType == "CARD1");
        Assert.Equal("2977338", card1.TerminalId);
        Assert.Equal("3169055788", card1.BusinessNo);
        var card2 = Assert.Single(rows, r => r.PayType == "CARD2");
        Assert.Equal("2977340", card2.TerminalId);
        Assert.Equal("3160326930", card2.BusinessNo);
    }
}
