using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Services;
using FishingMartPos.Tests.Fakes;
using FishingMartPos.ViewModels;
using Xunit;

namespace FishingMartPos.Tests.ViewModels;

public class CodeManageViewModelTests
{
    private static Dictionary<string, IReadOnlyList<CodeItem>> SampleCodes() => new()
    {
        ["POSCAT"] = new List<CodeItem>
        {
            new() { Code = "BAIT", Name = "미끼", SortNo = 1 },
            new() { Code = "FLOAT", Name = "찌세트", SortNo = 2 },
            new() { Code = "HAT", Name = "모자", SortNo = 3 },
        },
    };

    private static (CodeManageViewModel vm, FakeCodeRepository codes, FakeProductRepository products, INavigationService navigation, SettingsViewModel settings)
        Create()
    {
        var products = new FakeProductRepository(new List<Product>
        {
            new() { Barcode = "8800000020001", PosCatCd = "BAIT", Name = "지렁이", Price = 5000, StockQty = 10 },
        });
        var codes = new FakeCodeRepository(SampleCodes());
        var navigation = new NavigationService();
        var settings = new SettingsViewModel(navigation, new MainMenuViewModel(new CurrentSession(), navigation));
        var vm = new CodeManageViewModel(codes, products, navigation, settings);
        return (vm, codes, products, navigation, settings);
    }

    [Fact]
    public async Task LoadAsync_FillsPosCatColumnFromRepository()
    {
        var (vm, _, _, _, _) = Create();

        await vm.LoadAsync();

        Assert.Single(vm.PosCatCodes, c => c.Code == "BAIT");
    }

    [Fact]
    public async Task AddPosCatCode_AddsToPosCatCodesAndRepositoryAndClearsInput()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        vm.NewPosCatCode = "TACKLE";
        vm.NewPosCatName = "채비";

        await vm.AddPosCatCodeCommand.ExecuteAsync(null);

        Assert.Contains(vm.PosCatCodes, c => c.Code == "TACKLE" && c.Name == "채비");
        Assert.Contains(await codes.GetByGroupAsync("POSCAT"), c => c.Code == "TACKLE");
        Assert.Equal(string.Empty, vm.NewPosCatCode);
        Assert.Equal(string.Empty, vm.NewPosCatName);
    }

    [Fact]
    public async Task AddPosCatCode_WithDuplicateCode_ShowsErrorAndDoesNotAddDuplicate()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        vm.NewPosCatCode = "BAIT";
        vm.NewPosCatName = "중복코드";

        await vm.AddPosCatCodeCommand.ExecuteAsync(null);

        Assert.Equal("이미 존재하는 코드입니다", vm.ErrorMessage);
        Assert.Single(vm.PosCatCodes, c => c.Code == "BAIT");
    }

    [Fact]
    public async Task AddPosCatCode_WithMissingName_ShowsErrorAndDoesNotAdd()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        vm.NewPosCatCode = "NEWCAT";
        vm.NewPosCatName = "";

        await vm.AddPosCatCodeCommand.ExecuteAsync(null);

        Assert.Equal("코드와 이름을 모두 입력해주세요", vm.ErrorMessage);
        Assert.DoesNotContain(vm.PosCatCodes, c => c.Code == "NEWCAT");
    }

    [Fact]
    public async Task DeletePosCatCode_WhenUnused_RemovesFromListAndRepository()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        await codes.AddAsync("POSCAT", "UNUSED", "미사용");
        var unusedCode = new CodeItem { Code = "UNUSED", Name = "미사용", SortNo = 9 };
        vm.PosCatCodes.Add(unusedCode);

        await vm.DeletePosCatCodeCommand.ExecuteAsync(unusedCode);

        Assert.DoesNotContain(vm.PosCatCodes, c => c.Code == "UNUSED");
        Assert.DoesNotContain(await codes.GetByGroupAsync("POSCAT"), c => c.Code == "UNUSED");
    }

    [Fact]
    public async Task DeletePosCatCode_WhenUsedByExistingProduct_ShowsErrorAndKeepsCode()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        var usedCode = vm.PosCatCodes.Single(c => c.Code == "BAIT");

        await vm.DeletePosCatCodeCommand.ExecuteAsync(usedCode);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains(vm.PosCatCodes, c => c.Code == "BAIT");
        Assert.Contains(await codes.GetByGroupAsync("POSCAT"), c => c.Code == "BAIT");
    }

    [Fact]
    public async Task MovePosCatCodeUp_SwapsWithPreviousItemAndPersistsOrder()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        var floatCode = vm.PosCatCodes.Single(c => c.Code == "FLOAT");

        await vm.MovePosCatCodeUpCommand.ExecuteAsync(floatCode);

        Assert.Equal(new[] { "FLOAT", "BAIT", "HAT" }, vm.PosCatCodes.Select(c => c.Code));
        var persisted = await codes.GetByGroupAsync("POSCAT");
        Assert.Equal(0, persisted.Single(c => c.Code == "FLOAT").SortNo);
        Assert.Equal(1, persisted.Single(c => c.Code == "BAIT").SortNo);
        Assert.Equal(2, persisted.Single(c => c.Code == "HAT").SortNo);
    }

    [Fact]
    public async Task MovePosCatCodeUp_OnFirstItem_DoesNothing()
    {
        var (vm, _, _, _, _) = Create();
        await vm.LoadAsync();
        var firstCode = vm.PosCatCodes.Single(c => c.Code == "BAIT");

        await vm.MovePosCatCodeUpCommand.ExecuteAsync(firstCode);

        Assert.Equal(new[] { "BAIT", "FLOAT", "HAT" }, vm.PosCatCodes.Select(c => c.Code));
    }

    [Fact]
    public async Task MovePosCatCodeDown_SwapsWithNextItemAndPersistsOrder()
    {
        var (vm, codes, _, _, _) = Create();
        await vm.LoadAsync();
        var baitCode = vm.PosCatCodes.Single(c => c.Code == "BAIT");

        await vm.MovePosCatCodeDownCommand.ExecuteAsync(baitCode);

        Assert.Equal(new[] { "FLOAT", "BAIT", "HAT" }, vm.PosCatCodes.Select(c => c.Code));
        var persisted = await codes.GetByGroupAsync("POSCAT");
        Assert.Equal(0, persisted.Single(c => c.Code == "FLOAT").SortNo);
        Assert.Equal(1, persisted.Single(c => c.Code == "BAIT").SortNo);
        Assert.Equal(2, persisted.Single(c => c.Code == "HAT").SortNo);
    }

    [Fact]
    public async Task MovePosCatCodeDown_OnLastItem_DoesNothing()
    {
        var (vm, _, _, _, _) = Create();
        await vm.LoadAsync();
        var lastCode = vm.PosCatCodes.Single(c => c.Code == "HAT");

        await vm.MovePosCatCodeDownCommand.ExecuteAsync(lastCode);

        Assert.Equal(new[] { "BAIT", "FLOAT", "HAT" }, vm.PosCatCodes.Select(c => c.Code));
    }

    [Fact]
    public void GoToSettings_NavigatesBackToSettingsViewModel()
    {
        var (vm, _, _, navigation, settings) = Create();

        vm.GoToSettingsCommand.Execute(null);

        Assert.Same(settings, navigation.CurrentViewModel);
    }
}
