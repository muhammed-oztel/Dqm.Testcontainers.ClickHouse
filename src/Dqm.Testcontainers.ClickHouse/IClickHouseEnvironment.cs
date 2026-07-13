namespace Dqm.Testcontainers.ClickHouse;

public enum Topology
{
    Single,
    Cluster
}

public interface IClickHouseEnvironment : IAsyncDisposable
{
    string ConnectionString { get; }

    string Version { get; }

    Topology Topology { get; }

    Task StartAsync(CancellationToken ct);

    Task BackupAsync(string toPath, CancellationToken ct);
}
