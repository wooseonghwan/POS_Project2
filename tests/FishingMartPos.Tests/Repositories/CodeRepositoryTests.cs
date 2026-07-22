using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Repositories;
using Xunit;

namespace FishingMartPos.Tests.Repositories;

public class CodeRepositoryTests
{
    private static ICodeRepository CreateRepository()
    {
        var config = AppConfig.Load(AppContext.BaseDirectory);
        var factory = new MySqlConnectionFactory(config);
        return new CodeRepository(factory);
    }

    [Fact]
    public async Task GetByGroup_ReturnsPosCatCodesInSortOrder()
    {
        var repository = CreateRepository();

        var codes = await repository.GetByGroupAsync("POSCAT");

        Assert.Equal("BAIT", codes[0].Code);
        Assert.Equal("미끼", codes[0].Name);
        Assert.Equal(8, codes.Count);
    }

    [Fact]
    public async Task AddAsync_ThenGetByGroup_ThenDeleteAsync_RoundTrips()
    {
        var repository = CreateRepository();
        const string codeGbn = "MAJOR";
        const string codeCd = "TEST_CD";

        await repository.AddAsync(codeGbn, codeCd, "테스트분류");
        var afterAdd = await repository.GetByGroupAsync(codeGbn);
        Assert.Contains(afterAdd, c => c.Code == codeCd && c.Name == "테스트분류");

        await repository.DeleteAsync(codeGbn, codeCd);
        var afterDelete = await repository.GetByGroupAsync(codeGbn);
        Assert.DoesNotContain(afterDelete, c => c.Code == codeCd);
    }
}
