using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class ActionLogRepositoryTests
{
    private const string TestPosCd = "9";

    private static (IActionLogRepository repo, IDbConnectionFactory factory) Create()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return (new ActionLogRepository(factory), factory);
    }

    [Fact]
    public async Task InsertAsync_ThenSearchAsync_ReturnsTheRow()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM action_log_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.InsertAsync(new ActionLogEntry
            {
                PosCd = TestPosCd, StaffCd = "ADMIN1", ActionType = "PRODUCT_SAVE",
                ActionDetail = "상품등록: 바코드=12345", ResultStatus = "SUCCESS",
            });

            var rows = await repo.SearchAsync(DateTime.Today, DateTime.Today.AddDays(1));

            var row = Assert.Single(rows.Where(r => r.PosCd == TestPosCd));
            Assert.Equal("PRODUCT_SAVE", row.ActionType);
            Assert.Equal("ADMIN1", row.StaffCd);
            Assert.Equal("SUCCESS", row.ResultStatus);
            Assert.Null(row.ErrorMessage);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM action_log_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task InsertAsync_WithFailure_StoresErrorMessage()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM action_log_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.InsertAsync(new ActionLogEntry
            {
                PosCd = TestPosCd, StaffCd = "ADMIN1", ActionType = "SALE_CARD",
                ActionDetail = "카드결제 승인거절", ResultStatus = "FAIL", ErrorMessage = "한도초과",
            });

            var rows = await repo.SearchAsync(DateTime.Today, DateTime.Today.AddDays(1), resultStatus: "FAIL");

            var row = Assert.Single(rows.Where(r => r.PosCd == TestPosCd));
            Assert.Equal("FAIL", row.ResultStatus);
            Assert.Equal("한도초과", row.ErrorMessage);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM action_log_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }

    [Fact]
    public async Task SearchAsync_FiltersByActionType()
    {
        var (repo, factory) = Create();
        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM action_log_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        try
        {
            await repo.InsertAsync(new ActionLogEntry { PosCd = TestPosCd, ActionType = "NAVIGATE", ActionDetail = "a" });
            await repo.InsertAsync(new ActionLogEntry { PosCd = TestPosCd, ActionType = "PRODUCT_SAVE", ActionDetail = "b" });

            var rows = await repo.SearchAsync(DateTime.Today, DateTime.Today.AddDays(1), actionType: "PRODUCT_SAVE");

            Assert.All(rows.Where(r => r.PosCd == TestPosCd), r => Assert.Equal("PRODUCT_SAVE", r.ActionType));
            Assert.Contains(rows, r => r.PosCd == TestPosCd && r.ActionType == "PRODUCT_SAVE");
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM action_log_tb WHERE pos_cd = @PosCd", new { PosCd = TestPosCd });
        }
    }
}
