using System.Windows;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Theme;
using FishingMartPos.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FishingMartPos;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        foreach (var (name, brush) in AppColors.BuildBrushes())
        {
            Resources[name] = brush;
        }

        var config = AppConfig.Load(AppContext.BaseDirectory);

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
        services.AddSingleton<IStaffRepository, StaffRepository>();
        services.AddSingleton<IPosTerminalRepository, PosTerminalRepository>();
        services.AddSingleton<ICurrentSession, CurrentSession>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<ICodeRepository, CodeRepository>();
        services.AddSingleton<ISalesRepository, SalesRepository>();
        services.AddSingleton<IHeldOrderRepository, HeldOrderRepository>();
        services.AddSingleton<IDelayProvider, DelayProvider>();
        services.AddSingleton<IPhotoPicker, WpfPhotoPicker>();
        services.AddSingleton<IProductPhotoStorage>(_ => new FileSystemProductPhotoStorage(AppContext.BaseDirectory));
        _services = services.BuildServiceProvider();

        var terminalRepository = _services.GetRequiredService<IPosTerminalRepository>();
        IReadOnlyList<PosTerminal> terminals = await terminalRepository.GetAllAsync();

        var staffRepository = _services.GetRequiredService<IStaffRepository>();
        var session = _services.GetRequiredService<ICurrentSession>();
        var navigation = _services.GetRequiredService<INavigationService>();
        var productRepository = _services.GetRequiredService<IProductRepository>();
        var codeRepository = _services.GetRequiredService<ICodeRepository>();
        var salesRepository = _services.GetRequiredService<ISalesRepository>();
        var heldOrderRepository = _services.GetRequiredService<IHeldOrderRepository>();
        var delayProvider = _services.GetRequiredService<IDelayProvider>();
        var photoPicker = _services.GetRequiredService<IPhotoPicker>();
        var photoStorage = _services.GetRequiredService<IProductPhotoStorage>();

        async Task<PosViewModel> CreatePosViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu);
            await vm.LoadAsync();
            return vm;
        }

        async Task<InventoryViewModel> CreateInventoryViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new InventoryViewModel(productRepository, codeRepository, session, navigation, mainMenu);
            vm.InventoryFormViewModelFactory = (inv, product) =>
                Task.FromResult(new InventoryFormViewModel(productRepository, codeRepository, photoPicker, photoStorage, delayProvider, navigation, inv, product));
            await vm.LoadAsync();
            return vm;
        }

        LoginViewModel CreateLoginViewModel() =>
            new(staffRepository, session, navigation, terminals, CreatePosViewModelAsync, CreateInventoryViewModelAsync);

        navigation.NavigateTo(CreateLoginViewModel());

        var mainWindow = new MainWindow(navigation);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
