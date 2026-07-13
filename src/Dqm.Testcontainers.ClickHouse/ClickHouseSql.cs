using ClickHouse.Driver.ADO;

namespace Dqm.Testcontainers.ClickHouse;

internal static class ClickHouseSql
{
    public static async Task ExecuteAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new ClickHouseConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task<T> ExecuteScalarAsync<T>(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new ClickHouseConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(ct);
        return (T)Convert.ChangeType(result!, typeof(T));
    }
}
