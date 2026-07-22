using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class SystemInfoRepositoryTests
{
    [Fact]
    public async Task GetAsync_ReturnsSeededRow()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ISystemInfoRepository repository = new SystemInfoRepository(factory);

        var result = await repository.GetAsync();

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.AppVersion));
        Assert.False(string.IsNullOrWhiteSpace(result.DbVersion));
    }
}
