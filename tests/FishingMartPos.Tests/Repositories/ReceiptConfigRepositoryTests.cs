using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class ReceiptConfigRepositoryTests
{
    private const string TestPosCd = "9";

    private static (IReceiptConfigRepository repo, IDbConnectionFactory factory) Create()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return (new ReceiptConfigRepository(factory), factory);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNoRowExists()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });

        var result = await repo.GetAsync(TestPosCd);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsSavedValues()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveAsync(new ReceiptConfig { PosCd = TestPosCd, HeaderText = "환영합니다", FooterText = "감사합니다" });

            var result = await repo.GetAsync(TestPosCd);

            Assert.NotNull(result);
            Assert.Equal("환영합니다", result!.HeaderText);
            Assert.Equal("감사합니다", result.FooterText);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task SaveAsync_Twice_OverwritesExistingRow()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveAsync(new ReceiptConfig { PosCd = TestPosCd, HeaderText = "A", FooterText = "B" });
            await repo.SaveAsync(new ReceiptConfig { PosCd = TestPosCd, HeaderText = "C", FooterText = "D" });

            var result = await repo.GetAsync(TestPosCd);

            Assert.Equal("C", result!.HeaderText);
            Assert.Equal("D", result.FooterText);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM receipt_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }
}
