using System.Security.Cryptography;
using System.Text;

namespace FishingMartPos.Security;

/// <summary>
/// PIN 해시. db/migrations/001_create_schema.sql의 SHA2(pin || ':' || Salt, 256)과
/// 알고리즘이 동일해야 한다 — 한쪽만 바꾸면 로그인이 깨진다.
/// </summary>
public static class PinHasher
{
    private const string Salt = "fishingmart-pos-salt";

    public static string Hash(string pin)
    {
        byte[] input = Encoding.UTF8.GetBytes($"{pin}:{Salt}");
        byte[] hashBytes = SHA256.HashData(input);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
