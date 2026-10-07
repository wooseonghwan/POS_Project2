using FishingMartPos.Configuration;
using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Services.Printing;

/// <summary>
/// 환경설정 &gt; 프린터설정에 저장된 값으로 영수증을 인쇄한다.
/// - PrinterPort가 COM 포트(예: COM1)면 그 시리얼 포트로 ESC/POS 명령을 직접 보낸다.
/// - 그 외에는 PrinterName(Windows에 등록된 프린터 이름)으로 RAW 전송한다.
/// 머리말/하단 문구는 환경설정 &gt; 영수증설정 화면에 저장된 값을 사용한다.
/// </summary>
public sealed class WindowsReceiptPrinter : IReceiptPrinter
{
    private readonly IPrinterConfigRepository _printerConfigRepository;
    private readonly ICurrentSession _session;
    private readonly AppConfig _config;

    public WindowsReceiptPrinter(IPrinterConfigRepository printerConfigRepository, ICurrentSession session, AppConfig config)
    {
        _printerConfigRepository = printerConfigRepository;
        _session = session;
        _config = config;
    }

    public async Task<bool> PrintAsync(ReceiptDocument document)
    {
        string? posCd = _session.CurrentTerminal?.PosCode;
        if (posCd is null)
        {
            PrintLog.Write("중단: 로그인된 단말 정보 없음");
            return false;
        }

        var config = await _printerConfigRepository.GetAsync(posCd);
        PrintLog.Write($"설정 조회: posCd={posCd}, PrinterPort='{config?.PrinterPort}', PrinterName='{config?.PrinterName}', baud={_config.PrinterBaudRate}");
        if (config is null)
        {
            PrintLog.Write("중단: 프린터 설정 행이 없음 (환경설정에서 저장 필요)");
            return false;
        }

        byte[] bytes = EscPosReceiptFormatter.Build(document);
        try
        {
            if (!string.IsNullOrWhiteSpace(config.PrinterPort) && config.PrinterPort.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                string port = config.PrinterPort.Trim().ToUpperInvariant();
                PrintLog.Write($"시리얼 전송 시도: {port}, {bytes.Length} bytes");
                bool ok = await Task.Run(() => SerialPrinterHelper.Send(port, _config.PrinterBaudRate, bytes));
                PrintLog.Write($"시리얼 전송 결과: {ok}");
                return ok;
            }

            if (!string.IsNullOrWhiteSpace(config.PrinterName))
            {
                string name = config.PrinterName;
                PrintLog.Write($"Windows 프린터 전송 시도: {name}, {bytes.Length} bytes");
                bool ok = await Task.Run(() => RawPrinterHelper.SendBytesToPrinter(name, bytes));
                PrintLog.Write($"Windows 프린터 전송 결과: {ok}");
                return ok;
            }

            PrintLog.Write("중단: 포트/프린터 이름 둘 다 비어 있음");
            return false;
        }
        catch (Exception ex)
        {
            PrintLog.Write($"예외: {ex}");
            return false;
        }
    }

}
