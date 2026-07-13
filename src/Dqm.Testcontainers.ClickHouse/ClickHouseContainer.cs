using System.Text;
using Testcontainers.ClickHouse;

namespace Dqm.Testcontainers.ClickHouse;

public sealed class ClickHouseContainer : IClickHouseEnvironment
{
    private readonly global::Testcontainers.ClickHouse.ClickHouseContainer _container;
    private readonly string? _restoreFromPath;

    public ClickHouseContainer(ClickHouseContainerConfiguration configuration)
    {
        Version = configuration.Image;
        _restoreFromPath = configuration.RestoreFromPath;

        var builder = new ClickHouseBuilder(configuration.Image);
        if (configuration.SchemaDir is not null && Directory.Exists(configuration.SchemaDir))
        {
            builder = builder.WithResourceMapping(configuration.SchemaDir, "/docker-entrypoint-initdb.d");
        }

        var backupDiskXml = Encoding.UTF8.GetBytes(BackupDiskConfigGenerator.BackupDiskXml());
        builder = builder.WithResourceMapping(backupDiskXml, "/etc/clickhouse-server/config.d/backup_disk.xml");

        _container = builder.Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public string Version { get; }

    public Topology Topology => Topology.Single;

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _container.StartAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EnvironmentProvisioningException(
                $"Failed to provision ClickHouse container (image: {Version}): {ex.Message}", ex);
        }

        if (_restoreFromPath is not null)
        {
            await RestoreAsync(_restoreFromPath, ct);
        }
    }

    public async Task BackupAsync(string toPath, CancellationToken ct)
    {
        var fileName = Path.GetFileName(toPath);

        try
        {
            await ClickHouseSql.ExecuteAsync(
                ConnectionString,
                $"BACKUP DATABASE default TO Disk('{BackupDiskConfigGenerator.DiskName}', '{fileName}')",
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EnvironmentProvisioningException($"Failed to back up ClickHouse database to '{toPath}': {ex.Message}", ex);
        }

        var bytes = await _container.ReadFileAsync($"{BackupDiskConfigGenerator.ContainerBackupPath}{fileName}", ct);

        var dir = Path.GetDirectoryName(toPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(toPath, bytes, ct);
    }

    private async Task RestoreAsync(string fromPath, CancellationToken ct)
    {
        var fileName = Path.GetFileName(fromPath);
        var bytes = await File.ReadAllBytesAsync(fromPath, ct);
        await _container.CopyAsync(bytes, $"{BackupDiskConfigGenerator.ContainerBackupPath}{fileName}", ct: ct);

        try
        {
            await ClickHouseSql.ExecuteAsync(
                ConnectionString,
                $"RESTORE DATABASE default FROM Disk('{BackupDiskConfigGenerator.DiskName}', '{fileName}')",
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EnvironmentProvisioningException($"Failed to restore backup '{fromPath}' into ClickHouse container: {ex.Message}", ex);
        }
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
