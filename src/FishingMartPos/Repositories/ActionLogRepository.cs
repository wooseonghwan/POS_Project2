using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class ActionLogRepository : IActionLogRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ActionLogRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertAsync(ActionLogEntry entry)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            INSERT INTO action_log_tb (pos_cd, staff_cd, action_type, action_detail, result_status, error_message)
            VALUES (@PosCd, @StaffCd, @ActionType, @ActionDetail, @ResultStatus, @ErrorMessage)
            """;
        await connection.ExecuteAsync(sql, entry);
    }

    public async Task<IReadOnlyList<ActionLogEntry>> SearchAsync(
        DateTime from, DateTime to, string? actionType = null, string? resultStatus = null)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT log_no AS LogNo, log_dt AS LogDt, pos_cd AS PosCd, staff_cd AS StaffCd,
                   action_type AS ActionType, action_detail AS ActionDetail,
                   result_status AS ResultStatus, error_message AS ErrorMessage
            FROM action_log_tb
            WHERE log_dt >= @From AND log_dt < @To
              AND (@ActionType IS NULL OR action_type = @ActionType)
              AND (@ResultStatus IS NULL OR result_status = @ResultStatus)
            ORDER BY log_dt DESC
            """;
        var rows = await connection.QueryAsync<ActionLogEntry>(
            sql, new { From = from, To = to, ActionType = actionType, ResultStatus = resultStatus });
        return rows.ToList();
    }
}
