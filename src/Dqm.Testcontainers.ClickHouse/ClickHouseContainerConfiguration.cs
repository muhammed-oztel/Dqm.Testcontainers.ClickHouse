namespace Dqm.Testcontainers.ClickHouse;

public sealed record ClickHouseContainerConfiguration
{
    public required string Image { get; init; }

    public string? SchemaDir { get; init; }

    public string? RestoreFromPath { get; init; }
}
