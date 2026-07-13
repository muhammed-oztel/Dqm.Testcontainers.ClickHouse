# Dqm.Testcontainers.ClickHouse

Testcontainers-based single-node and multi-shard ClickHouse test environments,
with native `BACKUP`/`RESTORE` support for seeding tests from a real snapshot.

## Install

```
dotnet add package Dqm.Testcontainers.ClickHouse
```

## Usage

```csharp
await using var container = new ClickHouseContainer(new ClickHouseContainerConfiguration
{
    Image = "clickhouse/clickhouse-server:24.12",
    SchemaDir = "schema/common",
    RestoreFromPath = "snapshot.zip"
});
await container.StartAsync(ct);
```

See `ClickHouseClusterContainer`/`ClickHouseClusterContainerConfiguration` for
multi-shard clusters.
