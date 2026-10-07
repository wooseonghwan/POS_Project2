using Microsoft.Extensions.Configuration;

namespace FishingMartPos.Configuration;

public sealed class AppConfig
{
    public required string ConnectionString { get; init; }
    public required string PosCode { get; init; }
    public bool KiccUseRealGateway { get; init; }
    public int KiccComPort { get; init; }
    public int KiccBaudRate { get; init; } = 57600;
    // 이 매장은 "단말기승인(부착형)"이 아니라 EasyCard2의 "PC결제(로컬 HTTP/JSONP)" 방식으로 설정되어
    // 있음이 실기기 테스트로 확인됨(EasyCard2 환경설정에 신용 IP/PORT 존재, 단말기연결은 사용안함).
    // 카드 승인/취소는 KiccPos.dll가 아니라 이 포트로 로컬 요청을 보내 처리한다.
    public int KiccHttpPort { get; init; } = 8080;
    // 영수증 프린터를 시리얼 포트(COM)로 직접 연결한 경우의 전송 속도. 프린터 DIP 스위치/설정과 맞춰야 한다.
    public int PrinterBaudRate { get; init; } = 9600;

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
        if (!int.TryParse(configuration["Kicc:HttpPort"], out int kiccHttpPort))
        {
            kiccHttpPort = 8080;
        }
        if (!int.TryParse(configuration["Printer:BaudRate"], out int printerBaudRate))
        {
            printerBaudRate = 9600;
        }

        return new AppConfig
        {
            ConnectionString = connectionString,
            PosCode = posCode,
            KiccUseRealGateway = kiccUseRealGateway,
            KiccComPort = kiccComPort,
            KiccBaudRate = kiccBaudRate,
            KiccHttpPort = kiccHttpPort,
            PrinterBaudRate = printerBaudRate,
        };
    }
}
