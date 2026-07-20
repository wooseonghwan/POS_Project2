using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;
using FishingMartPos.Security;

namespace FishingMartPos.Repositories;

public sealed class StaffRepository : IStaffRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StaffRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Staff?> FindByPinAsync(string pin)
    {
        string pinHash = PinHasher.Hash(pin);

        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT staff_cd AS StaffCode, staff_name AS StaffName, role AS Role, use_yn AS UseYn
            FROM staff_tb
            WHERE pin_hash = @PinHash AND use_yn = 'Y'
            LIMIT 1
            """;

        return await connection.QuerySingleOrDefaultAsync<Staff>(sql, new { PinHash = pinHash });
    }
}
