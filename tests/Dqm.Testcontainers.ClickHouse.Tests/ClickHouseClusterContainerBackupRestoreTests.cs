namespace Dqm.Testcontainers.ClickHouse.Tests;

// Requires a running Docker daemon; spins up real ClickHouse + Keeper containers.
public class ClickHouseClusterContainerBackupRestoreTests
{
    private static ClusterDefinition TestCluster => new(
        "dqm_test_cluster",
        new[]
        {
            new ShardDefinition("shard1", new[] { new ReplicaDefinition("replica1") }),
            new ShardDefinition("shard2", new[] { new ReplicaDefinition("replica1") })
        });

    [Fact]
    public async Task BackupThenRestore_PreservesDataAcrossAllShards()
    {
        var backupDir = Path.Combine(Path.GetTempPath(), $"dqm-cluster-test-{Guid.NewGuid():N}");
        try
        {
            var cluster = TestCluster;

            await using (var source = new ClickHouseClusterContainer(new ClickHouseClusterContainerConfiguration
            {
                Image = "clickhouse/clickhouse-server:24.12",
                KeeperImage = "clickhouse/clickhouse-keeper:24.12",
                Cluster = cluster
            }))
            {
                await source.StartAsync(CancellationToken.None);

                await ClickHouseSql.ExecuteAsync(
                    source.ConnectionString,
                    $"CREATE TABLE widgets_local ON CLUSTER '{cluster.Name}' (id UInt32, name String) ENGINE = MergeTree ORDER BY id",
                    CancellationToken.None);
                await ClickHouseSql.ExecuteAsync(
                    source.ConnectionString,
                    $"CREATE TABLE widgets ON CLUSTER '{cluster.Name}' AS widgets_local ENGINE = Distributed('{cluster.Name}', default, widgets_local, id)",
                    CancellationToken.None);
                await ClickHouseSql.ExecuteAsync(
                    source.ConnectionString,
                    "INSERT INTO widgets VALUES (1,'a'),(2,'b'),(3,'c'),(4,'d'),(5,'e'),(6,'f')",
                    CancellationToken.None);

                await source.BackupAsync(backupDir, CancellationToken.None);
            }

            var backupFiles = Directory.GetFiles(backupDir, "*.zip");
            Assert.Equal(2, backupFiles.Length);

            await using var restored = new ClickHouseClusterContainer(new ClickHouseClusterContainerConfiguration
            {
                Image = "clickhouse/clickhouse-server:24.12",
                KeeperImage = "clickhouse/clickhouse-keeper:24.12",
                Cluster = cluster,
                RestoreFromPath = backupDir
            });
            await restored.StartAsync(CancellationToken.None);

            var count = await ClickHouseSql.ExecuteScalarAsync<ulong>(
                restored.ConnectionString, "SELECT count() FROM widgets", CancellationToken.None);

            Assert.Equal(6UL, count);
        }
        finally
        {
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, recursive: true);
            }
        }
    }
}
