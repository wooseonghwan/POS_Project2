using System.Runtime.InteropServices;

namespace FishingMartPos.Services.Printing;

/// <summary>
/// Windows에 등록된 프린터(드라이버 종류/연결 방식 무관 — USB, 공유, 시리얼 전부 동일하게 동작)에
/// RAW 바이트를 그대로 흘려보낸다. 영수증 프린터에 ESC/POS raw 명령을 보낼 때 표준적으로 쓰이는 방식이다.
/// </summary>
internal static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] ref DOCINFOA pDocInfo);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);

    public static bool SendBytesToPrinter(string printerName, byte[] bytes)
    {
        if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
        {
            return false;
        }

        try
        {
            var docInfo = new DOCINFOA
            {
                pDocName = "FishingMartPos Receipt",
                pOutputFile = null,
                pDataType = "RAW",
            };

            if (!StartDocPrinter(hPrinter, 1, ref docInfo))
            {
                return false;
            }

            try
            {
                if (!StartPagePrinter(hPrinter))
                {
                    return false;
                }

                try
                {
                    return WritePrinter(hPrinter, bytes, bytes.Length, out _);
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }
}
