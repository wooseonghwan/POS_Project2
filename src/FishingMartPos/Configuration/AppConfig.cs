using Microsoft.Extensions.Configuration;

namespace FishingMartPos.Configuration;

public sealed class AppConfig
{
    public required string ConnectionString { get; init; }
    public required string PosCode { get; init; }

    public static AppConfig Load(string basePath)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .Build();

        string? connectionString = configuration["Database:ConnectionString"];
        string? posCode = configuration["Terminal:PosCode"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database:ConnectionString 설정이 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(posCode))
        {
            throw new InvalidOperationException("Terminal:PosCode 설정이 없습니다.");
        }

        return new AppConfig { ConnectionString = connectionString, PosCode = posCode };
    }
}
