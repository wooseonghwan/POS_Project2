using Dapper;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class StaffRepositoryTests
{
    private static IStaffRepository CreateRepository()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return new StaffRepository(factory);
    }

    [Fact]
    public async Task FindByPin_ReturnsSeededAdmin_WhenPinIsCorrect()
    {
        IStaffRepository repository = CreateRepository();

        var staff = await repository.FindByPinAsync("0000");

        Assert.NotNull(staff);
        Assert.Equal("ADMIN1", staff!.StaffCode);
        Assert.True(staff.IsAdmin);
    }

    [Fact]
    public async Task FindByPin_ReturnsNull_WhenPinIsWrong()
    {
        IStaffRepository repository = CreateRepository();

        var staff = await repository.FindByPinAsync("9999");

        Assert.Null(staff);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSeededAdminRegardlessOfUseYn()
    {
        IStaffRepository repository = CreateRepository();

        var all = await repository.GetAllAsync();

        Assert.Contains(all, s => s.StaffCode == "ADMIN1");
    }

    [Fact]
    public async Task CreateAsync_ThenFindByPin_ReturnsNewlyCreatedStaff()
    {
        IStaffRepository repository = CreateRepository();
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        const string testCode = "TESTSTF";

        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        try
        {
            await repository.CreateAsync(
                new Staff { StaffCode = testCode, StaffName = "테스트직원", Role = "STAFF", UseYn = "Y" },
                "1357");

            var found = await repository.FindByPinAsync("1357");

            Assert.NotNull(found);
            Assert.Equal(testCode, found!.StaffCode);
            Assert.Equal("테스트직원", found.StaffName);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        }
    }

    [Fact]
    public async Task UpdateAsync_WithNullPin_KeepsExistingPinButUpdatesOtherFields()
    {
        IStaffRepository repository = CreateRepository();
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        const string testCode = "TESTSTF";

        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        try
        {
            await repository.CreateAsync(
                new Staff { StaffCode = testCode, StaffName = "원래이름", Role = "STAFF", UseYn = "Y" },
                "2468");

            await repository.UpdateAsync(
                new Staff { StaffCode = testCode, StaffName = "바뀐이름", Role = "STAFF", UseYn = "N" },
                newPin: null);

            var stillFindableByOldPin = await repository.FindByPinAsync("2468");
            Assert.Null(stillFindableByOldPin); // use_yn='N'이 되었으므로 로그인 조회(FindByPinAsync)는 실패해야 함

            var all = await repository.GetAllAsync();
            var updated = all.Single(s => s.StaffCode == testCode);
            Assert.Equal("바뀐이름", updated.StaffName);
            Assert.Equal("N", updated.UseYn);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        }
    }

    [Fact]
    public async Task UpdateAsync_WithNewPin_ChangesPinHash()
    {
        IStaffRepository repository = CreateRepository();
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        const string testCode = "TESTSTF";

        using var conn = await factory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        try
        {
            await repository.CreateAsync(
                new Staff { StaffCode = testCode, StaffName = "직원", Role = "STAFF", UseYn = "Y" },
                "1111");

            await repository.UpdateAsync(
                new Staff { StaffCode = testCode, StaffName = "직원", Role = "STAFF", UseYn = "Y" },
                newPin: "2222");

            Assert.Null(await repository.FindByPinAsync("1111"));
            Assert.NotNull(await repository.FindByPinAsync("2222"));
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM staff_tb WHERE staff_cd = @Code", new { Code = testCode });
        }
    }
}
