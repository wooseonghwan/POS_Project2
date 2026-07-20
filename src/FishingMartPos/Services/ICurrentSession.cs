using FishingMartPos.Models;

namespace FishingMartPos.Services;

public interface ICurrentSession
{
    Staff? CurrentStaff { get; }
    PosTerminal? CurrentTerminal { get; }
    bool IsSignedIn { get; }

    void SignIn(Staff staff, PosTerminal terminal);
    void SignOut();
}
