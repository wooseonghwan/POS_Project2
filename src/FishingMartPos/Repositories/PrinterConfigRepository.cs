using Dapper;
using FishingMartPos.Data;
using FishingMartPos.Models;

namespace FishingMartPos.Repositories;

public sealed class PrinterConfigRepository : IPrinterConfigRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PrinterConfigRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PrinterConfig?> GetAsync(string posCd)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            SELECT pos_cd AS PosCd, printer_port AS PrinterPort, printer_name AS PrinterName,
                   CASE WHEN drawer_kick_enabled = 'Y' THEN 1 ELSE 0 END AS DrawerKickEnabled
            FROM printer_config_tb
            WHERE pos_cd = @PosCd
            LIMIT 1
            """;
        return await connection.QuerySingleOrDefaultAsync<PrinterConfig>(sql, new { PosCd = posCd });
    }

    public async Task SaveAsync(PrinterConfig config)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        const string sql = """
            INSERT INTO printer_config_tb (pos_cd, printer_port, printer_name, drawer_kick_enabled)
            VALUES (@PosCd, @PrinterPort, @PrinterName, @DrawerKickEnabled)
            ON DUPLICATE KEY UPDATE
                printer_port = VALUES(printer_port), printer_name = VALUES(printer_name),
                drawer_kick_enabled = VALUES(drawer_kick_enabled)
            """;
        await connection.ExecuteAsync(sql, new
        {
            config.PosCd,
            config.PrinterPort,
            config.PrinterName,
            DrawerKickEnabled = config.DrawerKickEnabled ? "Y" : "N",
        });
    }
}
