namespace FishingMartPos.Services.Kicc;

public interface IKiccPosClient
{
    Task<bool> ConnectAsync();
    void Disconnect();
    Task<KiccRawResponse> RequestAsync(int cmd, int gcd, int jcd, string sendData, int maxPollAttempts = 600);
}
