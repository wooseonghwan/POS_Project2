using System.Windows;
using FishingMartPos.Configuration;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Navigation;
using FishingMartPos.Repositories;
using FishingMartPos.Services;
using FishingMartPos.Services.Kicc;
using FishingMartPos.Theme;
using FishingMartPos.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FishingMartPos;

public partial class App : Application
{
    private ServiceProvider? _services;
    private IKiccPosClient? _kiccPosClient;

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
        services.AddSingleton<IPrinterConfigRepository, PrinterConfigRepository>();
        services.AddSingleton<IReceiptConfigRepository, ReceiptConfigRepository>();
        services.AddSingleton<ISystemInfoRepository, SystemInfoRepository>();
        services.AddSingleton<ISalesRepository, SalesRepository>();
        services.AddSingleton<IHeldOrderRepository, HeldOrderRepository>();
        services.AddSingleton<IVanConfigRepository, VanConfigRepository>();
        services.AddSingleton<IDelayProvider, DelayProvider>();
        services.AddSingleton<IVanOutcomeProvider, RandomVanOutcomeProvider>();
        // 실제 KICC 로컬 에이전트 연동 시 IVanPaymentGateway 구현체만 교체(예: KiccVanPaymentGateway)
        services.AddSingleton<IVanPaymentGateway, StubVanPaymentGateway>();
        services.AddSingleton<ICashReceiptGateway, StubCashReceiptGateway>();
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
        var vanConfigRepository = _services.GetRequiredService<IVanConfigRepository>();
        var printerConfigRepository = _services.GetRequiredService<IPrinterConfigRepository>();
        var receiptConfigRepository = _services.GetRequiredService<IReceiptConfigRepository>();
        var systemInfoRepository = _services.GetRequiredService<ISystemInfoRepository>();
        var delayProvider = _services.GetRequiredService<IDelayProvider>();
        IVanPaymentGateway vanGateway = _services.GetRequiredService<IVanPaymentGateway>(); // StubVanPaymentGateway (기본값)
        IKiccPosClient? kiccPosClient = null;
        if (config.KiccUseRealGateway)
        {
            var merchantRows = await vanConfigRepository.GetByPosCodeAsync(config.PosCode);
            var merchantsByPayType = merchantRows.ToDictionary(
                r => r.PayType,
                r => new KiccMerchantConfig(r.PayType, r.TerminalId ?? string.Empty, r.BusinessNo ?? string.Empty));

            var realClient = new KiccPosClient(config.KiccComPort, config.KiccBaudRate);
            await realClient.ConnectAsync(); // 연결 실패해도 앱은 계속 기동 — 카드결제 시점에 자연스럽게 실패 처리됨
            kiccPosClient = realClient;
            vanGateway = new KiccVanPaymentGateway(realClient, merchantsByPayType);
        }
        _kiccPosClient = kiccPosClient;
        var photoPicker = _services.GetRequiredService<IPhotoPicker>();
        var photoStorage = _services.GetRequiredService<IProductPhotoStorage>();

        async Task<PosViewModel> CreatePosViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu, vanGateway, kiccPosClient);
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

        async Task<SalesReportViewModel> CreateSalesReportViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new SalesReportViewModel(salesRepository, navigation, mainMenu);
            await vm.LoadAsync();
            return vm;
        }

        async Task<StaffListViewModel> CreateStaffListViewModelAsync(SettingsViewModel settings)
        {
            var vm = new StaffListViewModel(staffRepository, navigation, settings);
            vm.StaffFormViewModelFactory = (list, staff) =>
                Task.FromResult(new StaffFormViewModel(staffRepository, delayProvider, navigation, list, staff));
            await vm.LoadAsync();
            return vm;
        }

        async Task<PrinterSettingsViewModel> CreatePrinterSettingsViewModelAsync(SettingsViewModel settings)
        {
            var vm = new PrinterSettingsViewModel(printerConfigRepository, session, delayProvider, navigation, settings);
            await vm.LoadAsync();
            return vm;
        }

        async Task<ReceiptSettingsViewModel> CreateReceiptSettingsViewModelAsync(SettingsViewModel settings)
        {
            var vm = new ReceiptSettingsViewModel(receiptConfigRepository, session, delayProvider, navigation, settings);
            await vm.LoadAsync();
            return vm;
        }

        async Task<SystemInfoViewModel> CreateSystemInfoViewModelAsync(SettingsViewModel settings)
        {
            var vm = new SystemInfoViewModel(systemInfoRepository, session, navigation, settings);
            await vm.LoadAsync();
            return vm;
        }

        async Task<CodeManageViewModel> CreateCodeManageViewModelAsync(SettingsViewModel settings)
        {
            var vm = new CodeManageViewModel(codeRepository, productRepository, navigation, settings);
            await vm.LoadAsync();
            return vm;
        }

        Task<SettingsViewModel> CreateSettingsViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new SettingsViewModel(navigation, mainMenu)
            {
                StaffListViewModelFactory = CreateStaffListViewModelAsync,
                PrinterSettingsViewModelFactory = CreatePrinterSettingsViewModelAsync,
                ReceiptSettingsViewModelFactory = CreateReceiptSettingsViewModelAsync,
                SystemInfoViewModelFactory = CreateSystemInfoViewModelAsync,
                CodeManageViewModelFactory = CreateCodeManageViewModelAsync,
            };
            return Task.FromResult(vm);
        }

        LoginViewModel CreateLoginViewModel() =>
            new(staffRepository, session, navigation, terminals, CreatePosViewModelAsync, CreateInventoryViewModelAsync, CreateSalesReportViewModelAsync, CreateSettingsViewModelAsync);

        navigation.NavigateTo(CreateLoginViewModel());

        var mainWindow = new MainWindow(navigation);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _kiccPosClient?.Disconnect();
        _services?.Dispose();
        base.OnExit(e);
    }
}
