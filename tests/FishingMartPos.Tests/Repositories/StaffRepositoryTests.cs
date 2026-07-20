using FishingMartPos.Configuration;
using FishingMartPos.Data;
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
}
