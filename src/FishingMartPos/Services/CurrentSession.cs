using FishingMartPos.Models;

namespace FishingMartPos.Services;

public sealed class CurrentSession : ICurrentSession
{
    public Staff? CurrentStaff { get; private set; }
    public PosTerminal? CurrentTerminal { get; private set; }
    public bool IsSignedIn => CurrentStaff is not null;

    public void SignIn(Staff staff, PosTerminal terminal)
    {
        CurrentStaff = staff;
        CurrentTerminal = terminal;
    }

    public void SignOut()
    {
        CurrentStaff = null;
        CurrentTerminal = null;
    }
}
