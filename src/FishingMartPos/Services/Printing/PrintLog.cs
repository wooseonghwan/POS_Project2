namespace FishingMartPos.Services.Printing;

/// <summary>
/// 영수증 인쇄 시도 기록. 앱 폴더의 printer-log.txt에 남긴다(실기기 진단용).
/// </summary>
internal static class PrintLog
{
    public static void Write(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "printer-log.txt");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
