using System.Text;

namespace Dqm.Testcontainers.ClickHouse;

internal static class ClusterConfigGenerator
{
    public static string RemoteServersXml(ClusterDefinition cluster, string interNodePassword)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<clickhouse>");
        sb.AppendLine("  <remote_servers replace=\"true\">");
        sb.AppendLine($"    <{cluster.Name}>");
        foreach (var shard in cluster.Shards)
        {
            sb.AppendLine("      <shard>");
            foreach (var replica in shard.Replicas)
            {
                sb.AppendLine("        <replica>");
                sb.AppendLine($"          <host>{NodeAlias(shard, replica)}</host>");
                sb.AppendLine("          <port>9000</port>");
                sb.AppendLine("          <user>default</user>");
                sb.AppendLine($"          <password>{interNodePassword}</password>");
                sb.AppendLine("        </replica>");
            }

            sb.AppendLine("      </shard>");
        }

        sb.AppendLine($"    </{cluster.Name}>");
        sb.AppendLine("  </remote_servers>");
        sb.AppendLine("</clickhouse>");
        return sb.ToString();
    }

    public static string MacrosXml(ShardDefinition shard, ReplicaDefinition replica) =>
        $"""
         <clickhouse>
           <macros>
             <shard>{shard.Name}</shard>
             <replica>{replica.Name}</replica>
           </macros>
         </clickhouse>
         """;

    public static string ZookeeperXml(string keeperAlias) =>
        $"""
         <clickhouse>
           <zookeeper>
             <node>
               <host>{keeperAlias}</host>
               <port>9181</port>
             </node>
           </zookeeper>
           <distributed_ddl>
             <path>/clickhouse/task_queue/ddl</path>
           </distributed_ddl>
         </clickhouse>
         """;

    public static string KeeperConfigXml() =>
        """
        <clickhouse>
            <logger>
                <level>trace</level>
                <log>/var/log/clickhouse-keeper/clickhouse-keeper.log</log>
                <errorlog>/var/log/clickhouse-keeper/clickhouse-keeper.err.log</errorlog>
                <size>1000M</size>
                <count>10</count>
            </logger>

            <listen_host>0.0.0.0</listen_host>
            <max_connections>4096</max_connections>

            <keeper_server>
                <tcp_port>9181</tcp_port>
                <server_id>1</server_id>
                <log_storage_path>/var/lib/clickhouse/coordination/logs</log_storage_path>
                <snapshot_storage_path>/var/lib/clickhouse/coordination/snapshots</snapshot_storage_path>

                <coordination_settings>
                    <operation_timeout_ms>10000</operation_timeout_ms>
                    <min_session_timeout_ms>10000</min_session_timeout_ms>
                    <session_timeout_ms>100000</session_timeout_ms>
                    <raft_logs_level>information</raft_logs_level>
                    <compress_logs>false</compress_logs>
                </coordination_settings>

                <hostname_checks_enabled>true</hostname_checks_enabled>
                <raft_configuration>
                    <server>
                        <id>1</id>
                        <hostname>localhost</hostname>
                        <port>9234</port>
                    </server>
                </raft_configuration>
            </keeper_server>
        </clickhouse>
        """;

    public static string NodeAlias(ShardDefinition shard, ReplicaDefinition replica) =>
        $"dqm-{shard.Name}-{replica.Name}".Replace('_', '-').ToLowerInvariant();
}
