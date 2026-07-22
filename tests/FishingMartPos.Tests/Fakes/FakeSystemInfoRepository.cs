using FishingMartPos.Models;
using FishingMartPos.Repositories;

namespace FishingMartPos.Tests.Fakes;

public sealed class FakeSystemInfoRepository : ISystemInfoRepository
{
    private readonly SystemInfo? _info;
    private readonly Exception? _exceptionToThrow;

    public FakeSystemInfoRepository(SystemInfo? info = null, Exception? exceptionToThrow = null)
    {
        _info = info;
        _exceptionToThrow = exceptionToThrow;
    }

    public Task<SystemInfo?> GetAsync()
    {
        if (_exceptionToThrow is not null)
        {
            throw _exceptionToThrow;
        }
        return Task.FromResult(_info);
    }
}
