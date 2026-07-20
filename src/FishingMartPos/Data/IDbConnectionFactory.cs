using System.Data;

namespace FishingMartPos.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateOpenConnection();
}
