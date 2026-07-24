using Microsoft.Extensions.Configuration;

namespace FishingMartPos.Configuration;

public sealed class AppConfig
{
    public required string ConnectionString { get; init; }
    public required string PosCode { get; init; }
    public bool KiccUseRealGateway { get; init; }
    public int KiccComPort { get; init; }
    public int KiccBaudRate { get; init; } = 57600;

    public static AppConfig Load(string basePath)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .Build();

        string? connectionString = configuration["Database:ConnectionString"];
        string? posCode = configuration["Terminal:PosCode"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database:ConnectionString 설정이 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(posCode))
        {
            throw new InvalidOperationException("Terminal:PosCode 설정이 없습니다.");
        }

        bool.TryParse(configuration["Kicc:UseRealGateway"], out bool kiccUseRealGateway);
        int.TryParse(configuration["Kicc:ComPort"], out int kiccComPort);
        if (!int.TryParse(configuration["Kicc:BaudRate"], out int kiccBaudRate))
        {
            kiccBaudRate = 57600;
        }

        return new AppConfig
        {
            ConnectionString = connectionString,
            PosCode = posCode,
            KiccUseRealGateway = kiccUseRealGateway,
            KiccComPort = kiccComPort,
            KiccBaudRate = kiccBaudRate,
        };
    }
}
