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

    private static void Log(string step)
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "startup-log.txt");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {step}{Environment.NewLine}");
        }
        catch
        {
            // 로깅 자체가 실패해도 앱 기동을 막지 않는다
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        Log("OnStartup 시작");
        base.OnStartup(e);

        try
        {
        // OnStartup이 async라 첫 await 지점에서 제어가 반환되는데, 그 시점에 열린 창이 하나도 없으면
        // 기본값(OnLastWindowClose)에서는 WPF가 "마지막 창이 닫혔다"고 오판해 앱을 조용히 종료시킬 수 있다.
        // (KICC 실제 하드웨어 연결처럼 초기화가 오래 걸리는 경로에서 재현됨 — DB만 조회하는 빠른 경로에선 안 드러남)
        // MainWindow.Show() 직후 원래 동작으로 되돌린다.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        foreach (var (name, brush) in AppColors.BuildBrushes())
        {
            Resources[name] = brush;
        }

        var config = AppConfig.Load(AppContext.BaseDirectory);
        Log($"설정 로드 완료 (KiccUseRealGateway={config.KiccUseRealGateway}, ComPort={config.KiccComPort}, BaudRate={config.KiccBaudRate})");

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
        services.AddSingleton<IReceiptPrinter, StubReceiptPrinter>();
        services.AddSingleton<ISignatureConverter, StubSignatureConverter>();
        services.AddSingleton<IPhotoPicker, WpfPhotoPicker>();
        services.AddSingleton<IProductPhotoStorage>(_ => new FileSystemProductPhotoStorage(AppContext.BaseDirectory));
        _services = services.BuildServiceProvider();
        Log("DI 컨테이너 빌드 완료");

        var terminalRepository = _services.GetRequiredService<IPosTerminalRepository>();
        IReadOnlyList<PosTerminal> terminals = await terminalRepository.GetAllAsync();
        Log($"단말 목록 조회 완료 ({terminals.Count}건)");

        var staffRepository = _services.GetRequiredService<IStaffRepository>();
        var session = _services.GetRequiredService<ICurrentSession>();
        var navigation = _services.GetRequiredService<INavigationService>();
        var productRepository = _services.GetRequiredService<IProductRepository>();
        var codeRepository = _services.GetRequiredService<ICodeRepository>();
        var salesRepository = _services.GetRequiredService<ISalesRepository>();
        var heldOrderRepository = _services.GetRequiredService<IHeldOrderRepository>();
        var vanConfigRepository = _services.GetRequiredService<IVanConfigRepository>();
        var receiptPrinter = _services.GetRequiredService<IReceiptPrinter>();
        ISignatureConverter signatureConverter = _services.GetRequiredService<ISignatureConverter>(); // StubSignatureConverter (기본값)
        var printerConfigRepository = _services.GetRequiredService<IPrinterConfigRepository>();
        var receiptConfigRepository = _services.GetRequiredService<IReceiptConfigRepository>();
        var systemInfoRepository = _services.GetRequiredService<ISystemInfoRepository>();
        var delayProvider = _services.GetRequiredService<IDelayProvider>();
        IVanPaymentGateway vanGateway = _services.GetRequiredService<IVanPaymentGateway>(); // StubVanPaymentGateway (기본값)
        ICashReceiptGateway cashReceiptGateway = _services.GetRequiredService<ICashReceiptGateway>(); // StubCashReceiptGateway (기본값)
        IKiccPosClient? kiccPosClient = null;
        var photoPicker = _services.GetRequiredService<IPhotoPicker>();
        var photoStorage = _services.GetRequiredService<IProductPhotoStorage>();

        async Task<PosViewModel> CreatePosViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PosViewModel(productRepository, codeRepository, salesRepository, heldOrderRepository, delayProvider, session, navigation, mainMenu, vanGateway, cashReceiptGateway, receiptPrinter, signatureConverter, CreatePaymentManagementViewModelAsync, kiccPosClient);
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

        async Task<PaymentManagementViewModel> CreatePaymentManagementViewModelAsync(MainMenuViewModel mainMenu)
        {
            var vm = new PaymentManagementViewModel(salesRepository, vanGateway, cashReceiptGateway, receiptPrinter, delayProvider, session, navigation, mainMenu);
            await vm.LoadAsync();
            return vm;
        }

        LoginViewModel CreateLoginViewModel() =>
            new(staffRepository, session, navigation, terminals, CreatePosViewModelAsync, CreateInventoryViewModelAsync, CreateSalesReportViewModelAsync, CreateSettingsViewModelAsync, CreatePaymentManagementViewModelAsync);

        Log("LoginViewModel 생성 직전");
        navigation.NavigateTo(CreateLoginViewModel());
        Log("navigation.NavigateTo 완료, MainWindow 생성 직전");

        var mainWindow = new MainWindow(navigation);
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        Log("MainWindow.Show() 완료");

        // KICC 네이티브 DLL 연결(KLoad)은 창이 뜬 뒤 백그라운드에서 시도한다.
        // 창을 띄우기 전에 이 호출을 하면(특히 UI 스레드가 첫 창을 그리며 다른 네이티브 DLL을 로드하는 시점과 겹치면)
        // 로더 락(loader lock) 데드락으로 앱이 창도 없이 영원히 멈추는 현상이 실기기에서 재현됨.
        if (config.KiccUseRealGateway)
        {
            _ = InitializeKiccGatewayAsync();
        }

        async Task InitializeKiccGatewayAsync()
        {
            try
            {
                Log("KICC 실연동 백그라운드 초기화 시작");
                var merchantRows = await vanConfigRepository.GetByPosCodeAsync(config.PosCode);
                Log($"van_config_tb 조회 완료 ({merchantRows.Count}건)");
                var merchantsByPayType = merchantRows.ToDictionary(
                    r => r.PayType,
                    r => new KiccMerchantConfig(r.PayType, r.TerminalId ?? string.Empty, r.BusinessNo ?? string.Empty));

                // 카드 승인/취소: 이 매장은 EasyCard2의 로컬 HTTP(PC결제) 방식이라 KiccPos.dll 없이 처리된다.
                vanGateway = new KiccHttpVanPaymentGateway(config.KiccHttpPort, merchantsByPayType);
                Log($"KICC HTTP 게이트웨이 구성 완료 (포트 {config.KiccHttpPort})");

                // 현금영수증/서명/돈통열기는 여전히 KiccPos.dll(단말기승인 부착형 API)을 사용한다.
                var realClient = new KiccPosClient(config.KiccComPort, config.KiccBaudRate);
                Log("KiccPosClient 생성 완료, ConnectAsync(KLoad) 호출 직전 (백그라운드)");
                bool connected = await realClient.ConnectAsync(); // 연결 실패해도 앱은 계속 기동 — 해당 기능만 자연스럽게 실패 처리됨
                Log($"ConnectAsync(KLoad) 반환됨: connected={connected}");
                kiccPosClient = realClient;
                _kiccPosClient = realClient;
                cashReceiptGateway = new KiccCashReceiptGateway(realClient, merchantsByPayType);
                signatureConverter = new KiccSignatureConverter();
                Log("KICC 게이트웨이 구성 완료 (백그라운드)");
            }
            catch (Exception ex)
            {
                Log($"KICC 백그라운드 초기화 중 예외: {ex}");
            }
        }
        }
        catch (Exception ex)
        {
            Log($"예외 발생: {ex}");
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _kiccPosClient?.Disconnect();
        _services?.Dispose();
        base.OnExit(e);
    }
}
