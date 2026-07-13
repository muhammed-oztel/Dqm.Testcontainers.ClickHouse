namespace Dqm.Testcontainers.ClickHouse;

public sealed record ClickHouseClusterContainerConfiguration
{
    public required string Image { get; init; }

    public required string KeeperImage { get; init; }

    public required ClusterDefinition Cluster { get; init; }

    public string? RestoreFromPath { get; init; }
}
