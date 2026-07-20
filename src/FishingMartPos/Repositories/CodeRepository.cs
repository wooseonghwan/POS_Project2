using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class CodeRepository : ICodeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CodeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<CodeItem>> GetByGroupAsync(string codeGbn)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT code_cd AS Code, code_nm AS Name, sort_no AS SortNo
            FROM code_tb
            WHERE code_gbn = @CodeGbn
            ORDER BY sort_no, code_cd
            """;

        var result = await connection.QueryAsync<CodeItem>(sql, new { CodeGbn = codeGbn });
        return result.ToList();
    }
}
