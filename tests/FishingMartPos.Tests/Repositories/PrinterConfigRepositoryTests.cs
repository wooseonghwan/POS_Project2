using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class PrinterConfigRepositoryTests
{
    private const string TestPosCd = "9";

    private static (IPrinterConfigRepository repo, IDbConnectionFactory factory) Create()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return (new PrinterConfigRepository(factory), factory);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNoRowExists()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });

        var result = await repo.GetAsync(TestPosCd);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsSavedValues()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveAsync(new PrinterConfig { PosCd = TestPosCd, PrinterPort = "COM3", PrinterName = "EPSON-TM88", DrawerKickEnabled = true });

            var result = await repo.GetAsync(TestPosCd);

            Assert.NotNull(result);
            Assert.Equal("COM3", result!.PrinterPort);
            Assert.Equal("EPSON-TM88", result.PrinterName);
            Assert.True(result.DrawerKickEnabled);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task SaveAsync_Twice_OverwritesExistingRow()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.SaveAsync(new PrinterConfig { PosCd = TestPosCd, PrinterPort = "COM3", PrinterName = "A", DrawerKickEnabled = true });
            await repo.SaveAsync(new PrinterConfig { PosCd = TestPosCd, PrinterPort = "COM4", PrinterName = "B", DrawerKickEnabled = false });

            var result = await repo.GetAsync(TestPosCd);

            Assert.Equal("COM4", result!.PrinterPort);
            Assert.Equal("B", result.PrinterName);
            Assert.False(result.DrawerKickEnabled);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM printer_config_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }
}
