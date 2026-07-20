using FishingMartPos.Models;
using FishingMartPos.Services;
using Xunit;

namespace FishingMartPos.Tests.Services;

public class CurrentSessionTests
{
    private static Staff SampleStaff() => new()
    {
        StaffCode = "ADMIN1", StaffName = "관리자", Role = "ADMIN", UseYn = "Y"
    };

    private static PosTerminal SampleTerminal() => new()
    {
        PosCode = "1", PosName = "POS1"
    };

    [Fact]
    public void IsSignedIn_IsFalse_BeforeSignIn()
    {
        ICurrentSession session = new CurrentSession();

        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public void SignIn_SetsStaffAndTerminal()
    {
        ICurrentSession session = new CurrentSession();

        session.SignIn(SampleStaff(), SampleTerminal());

        Assert.True(session.IsSignedIn);
        Assert.Equal("ADMIN1", session.CurrentStaff?.StaffCode);
        Assert.Equal("1", session.CurrentTerminal?.PosCode);
    }

    [Fact]
    public void SignOut_ClearsStaffAndTerminal()
    {
        ICurrentSession session = new CurrentSession();
        session.SignIn(SampleStaff(), SampleTerminal());

        session.SignOut();

        Assert.False(session.IsSignedIn);
        Assert.Null(session.CurrentStaff);
        Assert.Null(session.CurrentTerminal);
    }
}
