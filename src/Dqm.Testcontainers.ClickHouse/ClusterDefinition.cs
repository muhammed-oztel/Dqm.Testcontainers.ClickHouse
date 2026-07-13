namespace Dqm.Testcontainers.ClickHouse;

public sealed record ClusterDefinition(string Name, IReadOnlyList<ShardDefinition> Shards)
{
    public IEnumerable<(ShardDefinition Shard, ReplicaDefinition Replica)> AllNodes =>
        Shards.SelectMany(shard => shard.Replicas.Select(replica => (shard, replica)));
}
