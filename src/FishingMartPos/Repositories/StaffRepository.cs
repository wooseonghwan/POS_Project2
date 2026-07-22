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

    public async Task<IReadOnlyList<Staff>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT staff_cd AS StaffCode, staff_name AS StaffName, role AS Role, use_yn AS UseYn
            FROM staff_tb
            ORDER BY staff_cd
            """;

        var result = await connection.QueryAsync<Staff>(sql);
        return result.ToList();
    }

    public async Task CreateAsync(Staff staff, string pin)
    {
        string pinHash = PinHasher.Hash(pin);
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            INSERT INTO staff_tb (staff_cd, staff_name, pin_hash, role, use_yn)
            VALUES (@StaffCode, @StaffName, @PinHash, @Role, @UseYn)
            """;
        await connection.ExecuteAsync(sql, new { staff.StaffCode, staff.StaffName, PinHash = pinHash, staff.Role, staff.UseYn });
    }

    public async Task UpdateAsync(Staff staff, string? newPin)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        if (string.IsNullOrWhiteSpace(newPin))
        {
            const string sql = """
                UPDATE staff_tb SET staff_name = @StaffName, role = @Role, use_yn = @UseYn
                WHERE staff_cd = @StaffCode
                """;
            await connection.ExecuteAsync(sql, new { staff.StaffCode, staff.StaffName, staff.Role, staff.UseYn });
        }
        else
        {
            string pinHash = PinHasher.Hash(newPin);
            const string sql = """
                UPDATE staff_tb SET staff_name = @StaffName, role = @Role, use_yn = @UseYn, pin_hash = @PinHash
                WHERE staff_cd = @StaffCode
                """;
            await connection.ExecuteAsync(sql, new { staff.StaffCode, staff.StaffName, staff.Role, staff.UseYn, PinHash = pinHash });
        }
    }
}
