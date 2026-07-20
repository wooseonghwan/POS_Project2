using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class CodeRepositoryTests
{
    [Fact]
    public async Task GetByGroup_ReturnsPosCatCodesInSortOrder()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        ICodeRepository repository = new CodeRepository(factory);

        var codes = await repository.GetByGroupAsync("POSCAT");

        Assert.Equal("BAIT", codes[0].Code);
        Assert.Equal("미끼", codes[0].Name);
        Assert.Equal(8, codes.Count);
    }
}
