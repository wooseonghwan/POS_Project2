using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public interface IPosTerminalRepository
{
    Task<IReadOnlyList<PosTerminal>> GetAllAsync();
}
