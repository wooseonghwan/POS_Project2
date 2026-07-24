using FishingMartPos.Services.Kicc;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeKiccPosClient : IKiccPosClient
{
    private readonly KiccRawResponse _response;
    public List<(int Cmd, int Gcd, int Jcd, string SendData)> Requests { get; } = new();

    public FakeKiccPosClient(KiccRawResponse response)
    {
        _response = response;
    }

    public Task<bool> ConnectAsync() => Task.FromResult(true);

    public void Disconnect() { }

    public Task<KiccRawResponse> RequestAsync(int cmd, int gcd, int jcd, string sendData)
    {
        Requests.Add((cmd, gcd, jcd, sendData));
        return Task.FromResult(_response);
    }
}
