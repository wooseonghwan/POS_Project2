using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace FishingMartPos.Services.Kicc;

public sealed class KiccPosClient : IKiccPosClient
{
    // 카드 삽입/PIN 입력을 사람이 하는 동안 수십 초 걸릴 수 있다는 모듈 API 문서 설명에 맞춰
    // 넉넉하게 잡는다(600 * 100ms = 60초). KReqCmd 자체가 이미 그 시간을 기다린 뒤 반환되는 것으로
    // 보이므로 실제로는 대부분 첫 폴링에서 바로 응답이 온다.
    private const int MaxPollAttempts = 600;
    private const int PollIntervalMs = 100;
    private const int ConnectTimeoutMs = 10_000;

    private static void Log(string step)
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "kicc-request-log.txt");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {step}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private readonly int _port;
    private readonly int _baud;

    // KICC 네이티브 DLL 호출은 전부 이 스레드 하나에서만 실행한다. Task.Run으로 그때그때 스레드풀 스레드를
    // 새로 만들면, 그 스레드 생성 시점이 네이티브 DLL의 스레드 초기화 코드와 겹쳐 프로세스 전체가 멈추는
    // 현상이 실기기에서 재현됨(로더 락 계열 데드락으로 추정). 스레드를 앱 수명 동안 하나만 만들어 재사용해서
    // 이 충돌 가능성 자체를 없앤다.
    private readonly BlockingCollection<Action> _workQueue = new();
    private readonly Thread _workerThread;

    static KiccPosClient()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public KiccPosClient(int port, int baud)
    {
        _port = port;
        _baud = baud;
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "KiccPosWorker" };
        _workerThread.SetApartmentState(ApartmentState.STA);
        _workerThread.Start();
    }

    private void WorkerLoop()
    {
        foreach (var action in _workQueue.GetConsumingEnumerable())
        {
            action();
        }
    }

    private Task<T> RunOnWorkerAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workQueue.Add(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    [DllImport("KiccPos.dll", EntryPoint = "KLoad", CharSet = CharSet.Ansi)]
    private static extern int KLoad(int pPort, int pBaud, byte[] pErrMsg);

    [DllImport("KiccPos.dll", EntryPoint = "KUnLoad", CharSet = CharSet.Ansi)]
    private static extern void KUnLoad();

    [DllImport("KiccPos.dll", EntryPoint = "KReqCmd", CharSet = CharSet.Ansi)]
    private static extern int KReqCmd(int CMD, int GCD, int JCD, string SendData, byte[] ErrMsg);

    [DllImport("KiccPos.dll", EntryPoint = "KGetEvent", CharSet = CharSet.Ansi)]
    private static extern int KGetEvent(ref int CMD, ref int GCD, ref int JCD, ref int RCD, byte[] RData, byte[] RHexData);

    public async Task<bool> ConnectAsync()
    {
        var loadTask = RunOnWorkerAsync(() =>
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

        var completed = await Task.WhenAny(loadTask, Task.Delay(ConnectTimeoutMs));
        if (completed != loadTask)
        {
            // KLoad가 응답 없이 걸려있는 경우 — 워커 스레드는 계속 대기하지만(강제 종료 불가),
            // 앱은 더 이상 기다리지 않고 "연결 실패"로 간주해 정상 기동을 진행한다.
            return false;
        }

        return await loadTask;
    }

    public void Disconnect()
    {
        try
        {
            RunOnWorkerAsync(() =>
            {
                KUnLoad();
                return true;
            }).Wait(2000);
        }
        catch (Exception)
        {
        }
        finally
        {
            _workQueue.CompleteAdding();
        }
    }

    public async Task<KiccRawResponse> RequestAsync(int cmd, int gcd, int jcd, string sendData)
    {
        try
        {
            Log($"KReqCmd 호출 직전: CMD=0x{cmd:X2} GCD=0x{gcd:X2} JCD=0x{jcd:X2} SendData={sendData}");
            var err = new byte[4096];
            int ret = await RunOnWorkerAsync(() => KReqCmd(cmd, gcd, jcd, sendData, err));
            string errText = Encoding.GetEncoding(949).GetString(err).TrimEnd('\0');
            Log($"KReqCmd 반환됨: ret={ret} err='{errText}'");

            if (ret == -2) return KiccRawResponse.Failure("응답 시간 초과");
            if (ret == -3) return KiccRawResponse.Failure("고객이 결제를 취소했습니다");
            if (ret != 0) return KiccRawResponse.Failure(errText);

            for (int attempt = 0; attempt < MaxPollAttempts; attempt++)
            {
                var (len, rcd, text) = await RunOnWorkerAsync(() =>
                {
                    int c = 0, g = 0, j = 0, code = 0;
                    var rData = new byte[2048];
                    var rHex = new byte[4096];
                    int eventLen = KGetEvent(ref c, ref g, ref j, ref code, rData, rHex);
                    string eventText = eventLen > 0 ? Encoding.GetEncoding(949).GetString(rData).TrimEnd('\0') : string.Empty;
                    return (eventLen, code, eventText);
                });

                if (len > 0)
                {
                    Log($"KGetEvent 응답 수신 (시도 {attempt + 1}/{MaxPollAttempts}): len={len} rcd=0x{rcd:X2} text={text}");
                    return rcd == 0x00 ? KiccRawResponse.Success(text) : KiccRawResponse.Failure(text);
                }
                if (attempt == 0 || (attempt + 1) % 20 == 0)
                {
                    Log($"KGetEvent 대기 중 (시도 {attempt + 1}/{MaxPollAttempts}, 자료없음)");
                }
                await Task.Delay(PollIntervalMs);
            }
            Log($"KGetEvent 폴링 {MaxPollAttempts}회 모두 자료없음 — 응답 시간 초과 처리");
            return KiccRawResponse.Failure("응답 시간 초과");
        }
        catch (Exception ex)
        {
            Log($"RequestAsync 예외: {ex}");
            return KiccRawResponse.Failure("단말기 통신 오류");
        }
    }
}
