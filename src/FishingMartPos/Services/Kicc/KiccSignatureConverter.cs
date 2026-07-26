using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FishingMartPos.Services.Kicc;

// Kicc_Bmp2SignDataN()의 정확한 파라미터 시그니처는 벤더 문서(전문스펙)에 없다.
// KiccPos.dll의 자매 함수들(KGetBmp/KSaveToBmp/KBmpFileToEpsonPrtData)이 전부
// "파일 경로 입력 → byte[] 출력 버퍼 → 길이(또는 -1) 반환" 패턴을 따르므로 동일하게
// 구현했다 — 실제 물리 단말기/Kicc.dll 없이는 이 시그니처를 검증할 수 없다.
public sealed class KiccSignatureConverter : ISignatureConverter
{
    [DllImport("Kicc.dll", EntryPoint = "Kicc_Bmp2SignDataN", CharSet = CharSet.Ansi)]
    private static extern int Kicc_Bmp2SignDataN(string bmpFilePath, byte[] signData);

    public Task<string?> ConvertToHexAsync(byte[] bmpBytes) => Task.Run(() =>
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"sign_{Guid.NewGuid():N}.bmp");
        try
        {
            File.WriteAllBytes(tempFile, bmpBytes);
            var signData = new byte[8192];
            int len = Kicc_Bmp2SignDataN(tempFile, signData);
            if (len <= 0) return (string?)null;
            return Encoding.ASCII.GetString(signData, 0, len);
        }
        catch (Exception)
        {
            return (string?)null;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    });
}
