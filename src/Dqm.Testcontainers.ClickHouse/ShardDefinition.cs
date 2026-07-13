namespace Dqm.Testcontainers.ClickHouse;

public sealed record ShardDefinition(string Name, IReadOnlyList<ReplicaDefinition> Replicas);
