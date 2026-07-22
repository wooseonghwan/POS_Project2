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
                   name AS Name, price AS Price, stock_qty AS StockQty, photo_path AS PhotoPath
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

    public async Task SaveAsync(Product product)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            INSERT INTO product_tb (barcode, major_cd, minor_cd, poscat_cd, name, price, stock_qty, photo_path, use_yn)
            VALUES (@Barcode, @MajorCd, @MinorCd, @PosCatCd, @Name, @Price, @StockQty, @PhotoPath, 'Y')
            ON DUPLICATE KEY UPDATE
                major_cd = VALUES(major_cd), minor_cd = VALUES(minor_cd), poscat_cd = VALUES(poscat_cd),
                name = VALUES(name), price = VALUES(price), stock_qty = VALUES(stock_qty),
                photo_path = VALUES(photo_path), use_yn = 'Y'
            """;
        await connection.ExecuteAsync(sql, product);
    }
}
