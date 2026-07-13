using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Dqm.Testcontainers.ClickHouse;

public sealed class ClickHouseClusterContainer : IClickHouseEnvironment
{
    private const string KeeperAlias = "dqm-keeper";
    private const int HttpPort = 8123;
    private const string Password = "dqm";

    private readonly string _image;
    private readonly ClusterDefinition _cluster;
    private readonly INetwork _network;
    private readonly IContainer _keeper;
    private readonly List<(ShardDefinition Shard, ReplicaDefinition Replica, IContainer Container)> _nodes = new();
    private readonly string? _restoreFromPath;

    public ClickHouseClusterContainer(ClickHouseClusterContainerConfiguration configuration)
    {
        _image = configuration.Image;
        _cluster = configuration.Cluster;
        _restoreFromPath = configuration.RestoreFromPath;
        Version = configuration.Image;

        _network = new NetworkBuilder().Build();

        var keeperConfigXml = Encoding.UTF8.GetBytes(ClusterConfigGenerator.KeeperConfigXml());

        _keeper = new ContainerBuilder(configuration.KeeperImage)
            .WithNetwork(_network)
            .WithNetworkAliases(KeeperAlias)
            .WithPortBinding(9181, true)
            .WithResourceMapping(keeperConfigXml, "/etc/clickhouse-keeper/keeper_config.xml")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(9181))
            .Build();

        var remoteServersXml = Encoding.UTF8.GetBytes(ClusterConfigGenerator.RemoteServersXml(configuration.Cluster, Password));
        var zookeeperXml = Encoding.UTF8.GetBytes(ClusterConfigGenerator.ZookeeperXml(KeeperAlias));
        var backupDiskXml = Encoding.UTF8.GetBytes(BackupDiskConfigGenerator.BackupDiskXml());

        foreach (var (shard, replica) in configuration.Cluster.AllNodes)
        {
            var alias = ClusterConfigGenerator.NodeAlias(shard, replica);
            var macrosXml = Encoding.UTF8.GetBytes(ClusterConfigGenerator.MacrosXml(shard, replica));

            var container = new ContainerBuilder(_image)
                .WithNetwork(_network)
                .WithNetworkAliases(alias)
                .WithPortBinding(HttpPort, true)
                .WithEnvironment("CLICKHOUSE_PASSWORD", Password)
                .WithResourceMapping(remoteServersXml, "/etc/clickhouse-server/config.d/remote_servers.xml")
                .WithResourceMapping(zookeeperXml, "/etc/clickhouse-server/config.d/zookeeper.xml")
                .WithResourceMapping(macrosXml, "/etc/clickhouse-server/config.d/macros.xml")
                .WithResourceMapping(backupDiskXml, "/etc/clickhouse-server/config.d/backup_disk.xml")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(HttpPort))
                .Build();

            _nodes.Add((shard, replica, container));
        }
    }

    public string ConnectionString =>
        $"Host={_nodes[0].Container.Hostname};Port={_nodes[0].Container.GetMappedPublicPort(HttpPort)};Protocol=http;Username=default;Password={Password};Database=default";

    private static string NodeConnectionString(IContainer container) =>
        $"Host={container.Hostname};Port={container.GetMappedPublicPort(HttpPort)};Protocol=http;Username=default;Password={Password};Database=default";

    public string Version { get; }

    public Topology Topology => Topology.Cluster;

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _network.CreateAsync(ct);
            await _keeper.StartAsync(ct);
            await WaitForKeeperReadinessAsync(ct);
            await Task.WhenAll(_nodes.Select(n => n.Container.StartAsync(ct)));
            await WaitForClusterReadinessAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EnvironmentProvisioningException(
                $"Failed to provision ClickHouse cluster '{_cluster.Name}' (image: {_image}): {ex.Message}", ex);
        }

        if (_restoreFromPath is not null)
        {
            await RestoreAsync(_restoreFromPath, ct);
        }
    }

    public async Task BackupAsync(string toDir, CancellationToken ct)
    {
        Directory.CreateDirectory(toDir);

        await Task.WhenAll(_nodes.Select(async n =>
        {
            var alias = ClusterConfigGenerator.NodeAlias(n.Shard, n.Replica);
            var fileName = $"{alias}.zip";

            try
            {
                await ClickHouseSql.ExecuteAsync(
                    NodeConnectionString(n.Container),
                    $"BACKUP DATABASE default TO Disk('{BackupDiskConfigGenerator.DiskName}', '{fileName}')",
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new EnvironmentProvisioningException(
                    $"Failed to back up ClickHouse database on node '{alias}' to '{toDir}': {ex.Message}", ex);
            }

            var bytes = await n.Container.ReadFileAsync($"{BackupDiskConfigGenerator.ContainerBackupPath}{fileName}", ct);
            await File.WriteAllBytesAsync(Path.Combine(toDir, fileName), bytes, ct);
        }));
    }

    private async Task RestoreAsync(string fromDir, CancellationToken ct)
    {
        var expectedFiles = _nodes
            .Select(n => (Node: n, Path: Path.Combine(fromDir, $"{ClusterConfigGenerator.NodeAlias(n.Shard, n.Replica)}.zip")))
            .ToList();

        var missing = expectedFiles.Where(f => !File.Exists(f.Path)).Select(f => f.Path).ToList();
        if (missing.Count > 0)
        {
            throw new EnvironmentProvisioningException(
                $"Failed to restore cluster backup from '{fromDir}': missing per-node file(s) {string.Join(", ", missing)}",
                new FileNotFoundException("Missing per-node backup file(s).", missing[0]));
        }

        await Task.WhenAll(expectedFiles.Select(async f =>
        {
            var alias = ClusterConfigGenerator.NodeAlias(f.Node.Shard, f.Node.Replica);
            var fileName = $"{alias}.zip";
            var bytes = await File.ReadAllBytesAsync(f.Path, ct);
            await f.Node.Container.CopyAsync(bytes, $"{BackupDiskConfigGenerator.ContainerBackupPath}{fileName}", ct: ct);

            try
            {
                await ClickHouseSql.ExecuteAsync(
                    NodeConnectionString(f.Node.Container),
                    $"RESTORE DATABASE default FROM Disk('{BackupDiskConfigGenerator.DiskName}', '{fileName}')",
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new EnvironmentProvisioningException($"Failed to restore backup '{f.Path}' into node '{alias}': {ex.Message}", ex);
            }
        }));
    }

    private async Task WaitForKeeperReadinessAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var result = await _keeper.ExecAsync(
                new[] { "/bin/sh", "-c", "echo ruok | nc -w 1 127.0.0.1 9181" }, ct);

            if (result.Stdout.Contains("imok"))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        throw new TimeoutException("Keeper did not become ready within 30 seconds.");
    }

    private async Task WaitForClusterReadinessAsync(CancellationToken ct)
    {
        var expectedNodeCount = _nodes.Count;
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var count = await ClickHouseSql.ExecuteScalarAsync<ulong>(
                    ConnectionString,
                    $"SELECT count() FROM system.clusters WHERE cluster = '{_cluster.Name}'",
                    ct);

                if (count >= (ulong)expectedNodeCount)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Cluster isn't queryable yet (connection refused, node not registered) — keep polling.
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new TimeoutException(
            $"Cluster '{_cluster.Name}' did not become ready within 60 seconds (expected {expectedNodeCount} nodes in system.clusters).");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, _, container) in _nodes)
        {
            await container.DisposeAsync();
        }

        await _keeper.DisposeAsync();
        await _network.DisposeAsync();
    }
}
