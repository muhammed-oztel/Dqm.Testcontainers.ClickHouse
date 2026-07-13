namespace Dqm.Testcontainers.ClickHouse.Tests;

// Requires a running Docker daemon; spins up real ClickHouse containers.
public class ClickHouseContainerBackupRestoreTests
{
    [Fact]
    public async Task BackupThenRestore_PreservesData()
    {
        var backupPath = Path.Combine(Path.GetTempPath(), $"dqm-test-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var source = new ClickHouseContainer(new ClickHouseContainerConfiguration
            {
                Image = "clickhouse/clickhouse-server:24.12"
            }))
            {
                await source.StartAsync(CancellationToken.None);

                await ClickHouseSql.ExecuteAsync(
                    source.ConnectionString,
                    "CREATE TABLE widgets (id UInt32, name String) ENGINE = MergeTree ORDER BY id",
                    CancellationToken.None);
                await ClickHouseSql.ExecuteAsync(
                    source.ConnectionString,
                    "INSERT INTO widgets VALUES (1, 'sprocket'), (2, 'gear')",
                    CancellationToken.None);

                await source.BackupAsync(backupPath, CancellationToken.None);
            }

            Assert.True(File.Exists(backupPath));

            await using var restored = new ClickHouseContainer(new ClickHouseContainerConfiguration
            {
                Image = "clickhouse/clickhouse-server:24.12",
                RestoreFromPath = backupPath
            });
            await restored.StartAsync(CancellationToken.None);

            var count = await ClickHouseSql.ExecuteScalarAsync<ulong>(
                restored.ConnectionString, "SELECT count() FROM widgets", CancellationToken.None);

            Assert.Equal(2UL, count);
        }
        finally
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
    }
}
