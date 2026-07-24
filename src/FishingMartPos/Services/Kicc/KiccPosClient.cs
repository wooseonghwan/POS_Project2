using System.Runtime.InteropServices;
using System.Text;

namespace FishingMartPos.Services.Kicc;

public sealed class KiccPosClient : IKiccPosClient
{
    private const int MaxPollAttempts = 50;
    private const int PollIntervalMs = 100;

    private readonly int _port;
    private readonly int _baud;

    static KiccPosClient()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public KiccPosClient(int port, int baud)
    {
        _port = port;
        _baud = baud;
    }

    [DllImport("KiccPos.dll", EntryPoint = "KLoad", CharSet = CharSet.Ansi)]
    private static extern int KLoad(int pPort, int pBaud, byte[] pErrMsg);

    [DllImport("KiccPos.dll", EntryPoint = "KUnLoad", CharSet = CharSet.Ansi)]
    private static extern void KUnLoad();

    [DllImport("KiccPos.dll", EntryPoint = "KReqCmd", CharSet = CharSet.Ansi)]
    private static extern int KReqCmd(int CMD, int GCD, int JCD, string SendData, byte[] ErrMsg);

    [DllImport("KiccPos.dll", EntryPoint = "KGetEvent", CharSet = CharSet.Ansi)]
    private static extern int KGetEvent(ref int CMD, ref int GCD, ref int JCD, ref int RCD, byte[] RData, byte[] RHexData);

    public Task<bool> ConnectAsync() => Task.Run(() =>
    {
        try
        {
            var err = new byte[4096];
            int ret = KLoad(_port, _baud, err);
            return ret == 0;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public void Disconnect()
    {
        try
        {
            KUnLoad();
        }
        catch (Exception)
        {
        }
    }

    public async Task<KiccRawResponse> RequestAsync(int cmd, int gcd, int jcd, string sendData)
    {
        try
        {
            var err = new byte[4096];
            int ret = await Task.Run(() => KReqCmd(cmd, gcd, jcd, sendData, err));

            if (ret == -2) return KiccRawResponse.Failure("응답 시간 초과");
            if (ret == -3) return KiccRawResponse.Failure("고객이 결제를 취소했습니다");
            if (ret != 0) return KiccRawResponse.Failure(Encoding.GetEncoding(949).GetString(err).TrimEnd('\0'));

            for (int attempt = 0; attempt < MaxPollAttempts; attempt++)
            {
                int c = 0, g = 0, j = 0, rcd = 0;
                var rData = new byte[2048];
                var rHex = new byte[4096];
                int len = KGetEvent(ref c, ref g, ref j, ref rcd, rData, rHex);
                if (len > 0)
                {
                    var text = Encoding.GetEncoding(949).GetString(rData).TrimEnd('\0');
                    return rcd == 0x00 ? KiccRawResponse.Success(text) : KiccRawResponse.Failure(text);
                }
                await Task.Delay(PollIntervalMs);
            }
            return KiccRawResponse.Failure("응답 시간 초과");
        }
        catch (Exception)
        {
            return KiccRawResponse.Failure("단말기 통신 오류");
        }
    }
}
