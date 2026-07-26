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
            // 128x32 벤더 예시 규격 기준으로도 실제 네이티브 인코딩 오버헤드를 확신할 수 없어
            // 넉넉한 여유(256KB)를 둔다 — 8192바이트는 렌더링된 서명 비트맵 크기에 비해 너무 작아
            // 네이티브 함수가 그 이상을 쓰면 힙 오버런으로 이어질 수 있었다.
            var signData = new byte[262144];
            int len = Kicc_Bmp2SignDataN(tempFile, signData);
            if (len <= 0 || len > signData.Length) return (string?)null;
            return Encoding.ASCII.GetString(signData, 0, len);
        }
        catch (Exception)
        {
            return (string?)null;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception)
            {
                // 임시 파일 삭제 실패는 변환 결과에 영향을 주지 않는다 — 네이티브 DLL이 파일 핸들을
                // 아직 쥐고 있는 경우 등, finally에서 예외가 나면 호출 체인 전체가 크래시할 수 있다.
            }
        }
    });
}
