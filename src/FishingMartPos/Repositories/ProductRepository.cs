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
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            SELECT barcode AS Barcode, poscat_cd AS PosCatCd,
                   name AS Name, price AS Price, stock_qty AS StockQty, photo_path AS PhotoPath,
                   (pos_grid_yn = 'Y') AS ShowInGrid
            FROM product_tb
            WHERE use_yn = 'Y'
            ORDER BY poscat_cd, name
            """;

        var result = await connection.QueryAsync<Product>(sql);
        return result.ToList();
    }

    public async Task DeactivateAsync(string barcode)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = "UPDATE product_tb SET use_yn = 'N' WHERE barcode = @Barcode";
        await connection.ExecuteAsync(sql, new { Barcode = barcode });
    }

    public async Task SaveAsync(Product product)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        const string sql = """
            INSERT INTO product_tb (barcode, poscat_cd, name, price, stock_qty, photo_path, use_yn, pos_grid_yn)
            VALUES (@Barcode, @PosCatCd, @Name, @Price, @StockQty, @PhotoPath, 'Y',
                    CASE WHEN @ShowInGrid THEN 'Y' ELSE 'N' END)
            ON DUPLICATE KEY UPDATE
                poscat_cd = VALUES(poscat_cd),
                name = VALUES(name), price = VALUES(price), stock_qty = VALUES(stock_qty),
                photo_path = VALUES(photo_path), use_yn = 'Y', pos_grid_yn = VALUES(pos_grid_yn)
            """;
        await connection.ExecuteAsync(sql, product);
    }
}
