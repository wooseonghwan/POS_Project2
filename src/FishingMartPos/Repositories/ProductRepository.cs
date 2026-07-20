using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Product>> GetActiveAsync()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT barcode AS Barcode, major_cd AS MajorCd, minor_cd AS MinorCd, poscat_cd AS PosCatCd,
                   name AS Name, price AS Price, stock_qty AS StockQty
            FROM product_tb
            WHERE use_yn = 'Y'
            ORDER BY poscat_cd, name
            """;

        var result = await connection.QueryAsync<Product>(sql);
        return result.ToList();
    }

    public async Task DeactivateAsync(string barcode)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = "UPDATE product_tb SET use_yn = 'N' WHERE barcode = @Barcode";
        await connection.ExecuteAsync(sql, new { Barcode = barcode });
    }
}
