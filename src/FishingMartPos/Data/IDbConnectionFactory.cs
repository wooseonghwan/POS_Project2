using System.Data;

namespace FishingMartPos.Data;

public interface IDbConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync();
}
