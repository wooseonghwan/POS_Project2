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
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT code_cd AS Code, code_nm AS Name, sort_no AS SortNo
            FROM code_tb
            WHERE code_gbn = @CodeGbn
            ORDER BY sort_no, code_cd
            """;

        var result = await connection.QueryAsync<CodeItem>(sql, new { CodeGbn = codeGbn });
        return result.ToList();
    }

    public async Task AddAsync(string codeGbn, string codeCd, string codeNm)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            INSERT INTO code_tb (code_gbn, code_cd, code_nm, sort_no)
            VALUES (@CodeGbn, @CodeCd, @CodeNm, 0)
            """;
        await connection.ExecuteAsync(sql, new { CodeGbn = codeGbn, CodeCd = codeCd, CodeNm = codeNm });
    }

    public async Task DeleteAsync(string codeGbn, string codeCd)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = "DELETE FROM code_tb WHERE code_gbn = @CodeGbn AND code_cd = @CodeCd";
        await connection.ExecuteAsync(sql, new { CodeGbn = codeGbn, CodeCd = codeCd });
    }

    public async Task UpdateSortOrderAsync(string codeGbn, IReadOnlyList<string> orderedCodeCds)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = "UPDATE code_tb SET sort_no = @SortNo WHERE code_gbn = @CodeGbn AND code_cd = @CodeCd";
        for (int i = 0; i < orderedCodeCds.Count; i++)
        {
            await connection.ExecuteAsync(sql, new { SortNo = i, CodeGbn = codeGbn, CodeCd = orderedCodeCds[i] });
        }
    }
}
