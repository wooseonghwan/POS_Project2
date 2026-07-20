using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class PosTerminalRepositoryTests
{
    [Fact]
    public async Task GetAll_ReturnsSeededTwoTerminals()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        IPosTerminalRepository repository = new PosTerminalRepository(factory);

        var terminals = await repository.GetAllAsync();

        Assert.Contains(terminals, t => t.PosCode == "1" && t.PosName == "POS1");
        Assert.Contains(terminals, t => t.PosCode == "2" && t.PosName == "POS2");
    }
}
