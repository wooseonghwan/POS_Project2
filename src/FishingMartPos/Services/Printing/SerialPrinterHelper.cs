using System.IO.Ports;

namespace FishingMartPos.Services.Printing;

/// <summary>
/// 영수증 프린터가 시리얼 포트(COM)에 직접 연결된 경우, ESC/POS 바이트를 그 포트로 그대로 보낸다.
/// Windows 프린터 등록 없이도 동작한다.
/// </summary>
internal static class SerialPrinterHelper
{
    public static bool Send(string portName, int baudRate, byte[] bytes)
    {
        try
        {
            using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                WriteTimeout = 5000,
            };
            port.Open();
            port.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
